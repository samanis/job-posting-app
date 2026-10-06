using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using JobPosting.Api.Contracts;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
namespace JobPosting.Api.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class ShutdownSignalTests(SignalFixture fixture) : IClassFixture<SignalFixture>
{
    [Theory]
    [InlineData("graceful-publication")]
    [InlineData("graceful-cleanup")]
    [InlineData("forced-publication")]
    [InlineData("forced-cleanup")]
    public async Task LinuxSignalsDrainOrLeaveExplicitlyUnresolvedRows(string mode)
    {
        var options = await fixture.Postgres.NewDatabaseAsync();
        await using var context = new PostingDbContext(options);
        var connection = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString()) { Host = fixture.Postgres.ContainerName, Port = 5432 }.ConnectionString;
        var settings = fixture.Broker.Settings();
        var container = "jobposting-stage7-" + Guid.NewGuid().ToString("N");
        try
        {
            await fixture.Command(["docker","run","--detach","--name",container,"--network",fixture.Network,"--publish","127.0.0.1::8080",
                "--env","PostingDatabase__ConnectionString","--env","RabbitMq__Password","--env","RabbitMq__UserName=jobposting",
                "--env","RabbitMq__HostName="+fixture.Broker.ContainerName,"--env","RabbitMq__Queue="+settings.Queue,"--env","RabbitMq__Exchange="+settings.Exchange,
                "--env","SIGNAL_TEST_MODE="+mode,"--env","ASPNETCORE_URLS=http://+:8080",fixture.Image], new() { ["PostingDatabase__ConnectionString"] = connection, ["RabbitMq__Password"] = settings.Password });
            var address = (await fixture.Command(["docker", "port", container, "8080/tcp"])).Trim();
            using var client = new HttpClient { BaseAddress = new Uri("http://" + address), Timeout = TimeSpan.FromSeconds(45) };
            var startup = Stopwatch.StartNew();
            while (true)
            {
                try { if ((await client.GetAsync("/health/live")).IsSuccessStatusCode) break; }
                catch (HttpRequestException) when (startup.Elapsed < TimeSpan.FromSeconds(30)) { }
                if (startup.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Signal test host did not start.");
                await Task.Delay(100);
            }
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/jobs") { Content = JsonContent.Create(new CreateJobRequest { Title = "Engineer", Department = "Engineering", Location = "Toronto", Description = "Plain text", SalaryMin = 10m, SalaryMax = 100m, ClosingDate = new(2028, 2, 29) }) };
            request.Headers.Add("Idempotency-Key", "signal-key"); var posting = client.SendAsync(request);
            var window = mode.Contains("cleanup", StringComparison.Ordinal) ? "CleanupWindow" : "PublicationWindow";
            var waiting = Stopwatch.StartNew();
            while (!(await fixture.Command(["docker", "logs", container])).Contains(window, StringComparison.Ordinal))
            { if (waiting.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Signal test never reached the failure window."); await Task.Delay(50); }
            Assert.Single(await context.Jobs.AsNoTracking().ToArrayAsync());
            var shutdown = Stopwatch.StartNew();
            var forced = mode.StartsWith("forced", StringComparison.Ordinal);
            if (forced) await fixture.Command(["docker", "kill", "--signal", "KILL", container]);
            else await fixture.Command(["docker", "stop", "--time", "45", container]);
            Assert.True(shutdown.Elapsed < TimeSpan.FromSeconds(30), "Graceful work exceeded the 30-second host timeout.");
            if (forced) await Assert.ThrowsAnyAsync<HttpRequestException>(async () => await posting);
            else { using var response = await posting; Assert.Equal(mode.Contains("cleanup", StringComparison.Ordinal) ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.Accepted, response.StatusCode); }
            var row = await context.Jobs.AsNoTracking().SingleOrDefaultAsync();
            if (mode == "graceful-cleanup") Assert.Null(row);
            else { Assert.NotNull(row); Assert.Equal(mode == "graceful-publication", row.PublishedAt.HasValue); }
            var logs = await fixture.Command(["docker", "logs", container]); Assert.DoesNotContain("PostingCleanupFailed", logs); Assert.DoesNotContain("Critical", logs);
            Assert.DoesNotContain(settings.Password, logs); Assert.DoesNotContain(connection, logs);
            if (mode == "graceful-publication")
            {
                await using var brokerConnection = await RabbitMqMessaging.CreateFactory(settings).CreateConnectionAsync(); await using var channel = await brokerConnection.CreateChannelAsync();
                var message = await channel.BasicGetAsync(settings.Queue, true); Assert.NotNull(message); Assert.Equal(row!.EventId.ToString("D"), message.BasicProperties.MessageId); Assert.Null(await channel.BasicGetAsync(settings.Queue, true));
            }
            var exit = int.Parse((await fixture.Command(["docker", "inspect", "--format", "{{.State.ExitCode}}", container])).Trim()); Assert.Equal(forced ? 137 : 0, exit);
        }
        finally { await fixture.Command(["docker", "rm", "--force", "--volumes", container], allowFailure: true); }
    }
}
public sealed class SignalFixture : IAsyncLifetime
{
    public PostgresFixture Postgres { get; } = new(); public RabbitMqFixture Broker { get; } = new();
    public string Network { get; } = "jobposting-stage7-" + Guid.NewGuid().ToString("N");
    public string Image { get; } = "jobposting-signal-test:" + Guid.NewGuid().ToString("N");
    public async Task InitializeAsync()
    {
        try
        {
            await Task.WhenAll(Postgres.InitializeAsync(), Broker.InitializeAsync());
            await Command(["docker", "network", "create", Network]);
            await Command(["docker", "network", "connect", Network, Postgres.ContainerName]); await Command(["docker", "network", "connect", Network, Broker.ContainerName]);
            var root = new DirectoryInfo(AppContext.BaseDirectory); while (root is not null && !File.Exists(Path.Combine(root.FullName, "JobBoard.slnx"))) root = root.Parent;
            if (root is null) throw new InvalidOperationException("Application root not found.");
            var output = Path.Combine(root.FullName, "tests", "JobPosting.Api.IntegrationTests", "TestResults", "signal-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
            await Command(["dotnet", "publish", Path.Combine(root.FullName, "tests", "JobPosting.ShutdownHarness"), "--configuration", "Release", "--no-restore", "--output", output]);
            await File.WriteAllTextAsync(Path.Combine(output, "Dockerfile"), "FROM mcr.microsoft.com/dotnet/aspnet@sha256:eaa79205c3ade4792a7f7bf310a3aac51fe0e1d91c44e40f70b7c6423d475fe0\nWORKDIR /app\nCOPY . .\nENTRYPOINT [\"dotnet\",\"JobPosting.ShutdownHarness.dll\"]\n");
            await Command(["docker", "build", "--tag", Image, output]);
        }
        catch { await DisposeAsync(); throw; }
    }
    public async Task DisposeAsync()
    { await Task.WhenAll(Postgres.DisposeAsync(), Broker.DisposeAsync()); await Command(["docker", "network", "rm", Network], allowFailure: true); await Command(["docker", "image", "rm", Image], allowFailure: true); }
    public async Task<string> Command(string[] arguments, Dictionary<string, string>? environment = null, bool allowFailure = false)
    {
        var info = new ProcessStartInfo(arguments[0]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }; foreach (var arg in arguments.Skip(1)) info.ArgumentList.Add(arg);
        if (environment is not null) foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Test command did not start."); var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync(); using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); } catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Signal fixture command exceeded two minutes."); }
        var result = await output; var failure = await error; if (process.ExitCode != 0 && !allowFailure) throw new InvalidOperationException("Signal fixture command failed: " + failure); return result + failure;
    }
}
