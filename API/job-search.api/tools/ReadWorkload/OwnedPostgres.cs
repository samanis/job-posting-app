using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Diagnostics;
using System.Globalization;
using JobSearch.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ReadWorkload;

public sealed class OwnedPostgres : IAsyncDisposable
{
    public string ContainerName => container;
    public const string Image = "postgres:18.6@sha256:5a5a84b19854a9ffaa54082c166ff4ec27473a361e496e5ea167f298f2da9722";
    private readonly string container = "jobsearch-workload-" + Guid.NewGuid().ToString("N");
    private readonly string password = Guid.NewGuid().ToString("N");
    private string adminConnection = "";

    public async Task InitializeAsync()
    {
        // Configure the driver's process-wide date mapping before opening even the administrative connection.
        _ = SearchPersistence.CreateOptions(new());
        try
        {
            await DockerAsync(["run", "--detach", "--rm", "--name", container,
                "--env", "POSTGRES_PASSWORD", "--env", "POSTGRES_USER=jobsearch", "--env", "POSTGRES_DB=search_admin",
                "--publish", "127.0.0.1::5432", Image], password);
            var address = (await DockerAsync(["port", container, "5432/tcp"])).Trim();
            var port = int.Parse(address.Split(':')[^1], CultureInfo.InvariantCulture);
            adminConnection = new NpgsqlConnectionStringBuilder
            {
                Host = "127.0.0.1",
                Port = port,
                Database = "search_admin",
                Username = "jobsearch",
                Password = password,
                Timeout = 2,
                CommandTimeout = 10,
                IncludeErrorDetail = false
            }.ConnectionString;
            var deadline = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    await using var connection = new NpgsqlConnection(adminConnection);
                    await connection.OpenAsync();
                    break;
                }
                catch (NpgsqlException) when (deadline.Elapsed < TimeSpan.FromSeconds(60))
                {
                    await Task.Delay(250);
                }
            }
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task<DbContextOptions<SearchDbContext>> NewDatabaseAsync(string? migration = null)
    {
        // Each case owns an isolated randomly named database inside this fixture's disposable container.
        var database = "search_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(adminConnection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE DATABASE {database}", connection);
        await command.ExecuteNonQueryAsync();
        var settings = new SearchDatabaseOptions
        {
            ConnectionString = new NpgsqlConnectionStringBuilder(adminConnection) { Database = database }.ConnectionString
        };
        var options = SearchPersistence.CreateOptions(settings);
        await using var context = new SearchDbContext(options);
        await context.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync(migration);
        return options;
    }

    public Task PauseAsync() => DockerAsync(["pause", container]);
    public Task ResumeAsync() => DockerAsync(["unpause", container]);
    public async ValueTask DisposeAsync() => await DockerAsync(["rm", "--force", "--volumes", container], allowMissing: true);

    private static async Task<string> DockerAsync(string[] arguments, string? password = null, bool allowMissing = false)
    {
        var start = new ProcessStartInfo("docker")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        if (password is not null) start.Environment["POSTGRES_PASSWORD"] = password;
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
