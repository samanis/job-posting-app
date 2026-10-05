using System.Diagnostics;
using System.Globalization;
using JobPosting.Api.Messaging;
using RabbitMQ.Client;

namespace JobPosting.Api.IntegrationTests;

public sealed class RabbitMqFixture : IAsyncLifetime
{
    public string ContainerName => container;
    public const string Image = "rabbitmq:4.3.6-management@sha256:8dd6e3570ddaa2ef82a6c3a8950e79c1f73893adfbdddbc374fc1f1ac8a0f5dd";
    private readonly string container = "jobposting-stage5-" + Guid.NewGuid().ToString("N");
    private readonly string password = Guid.NewGuid().ToString("N");
    private int port;
    public RabbitMqOptions Settings()
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new() { HostName = "127.0.0.1", Port = port, UserName = "jobposting", Password = password,
            Exchange = "job-post-exchange-" + suffix, Queue = "job-post-queue-" + suffix };
    }
    public async Task InitializeAsync()
    {
        try
        {
            var reservation = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            reservation.Start();
            port = ((System.Net.IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            await DockerAsync(["run", "--detach", "--rm", "--name", container, "--env", "RABBITMQ_DEFAULT_PASS",
                "--env", "RABBITMQ_DEFAULT_USER=jobposting", "--publish", $"127.0.0.1:{port}:5672", Image], password);
            var address = (await DockerAsync(["port", container, "5672/tcp"])).Trim();
            port = int.Parse(address.Split(':')[^1], CultureInfo.InvariantCulture);
            await WaitReadyAsync();
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task RestartAsync() { await DockerAsync(["restart", container]); await WaitReadyAsync(); }
    private async Task WaitReadyAsync()
    {
        var deadline = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                await using var connection = await RabbitMqMessaging.CreateFactory(Settings()).CreateConnectionAsync();
                return;
            }
            catch (Exception) when (deadline.Elapsed < TimeSpan.FromSeconds(60)) { await Task.Delay(250); }
        }
    }
    public Task DisposeAsync() => DockerAsync(["rm", "--force", "--volumes", container], allowMissing: true);
    private static async Task<string> DockerAsync(string[] arguments, string? password = null, bool allowMissing = false)
    {
        var start = new ProcessStartInfo("docker")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (password is not null) start.Environment["RABBITMQ_DEFAULT_PASS"] = password;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Docker CLI could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Docker integration fixture command exceeded two minutes.");
        }
        if (process.ExitCode != 0)
        {
            var message = await error;
            if (allowMissing && message.Contains("No such container: " + arguments[^1], StringComparison.Ordinal)) return "";
            throw new InvalidOperationException("Docker integration fixture failed: " + message);
        }
        return await output;
    }
}
