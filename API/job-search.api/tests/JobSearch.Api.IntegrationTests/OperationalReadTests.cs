using JobSearch.Api.Persistence;
using JobSearch.Api.Search;
using JobSearch.Api.Messaging;
using JobSearch.Api.Diagnostics;
using JobSearch.Api.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using System.Net;
namespace JobSearch.Api.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class OperationalReadTests(PostgresFixture pg, RabbitMqFixture rabbit) : IClassFixture<PostgresFixture>, IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task RealSearchHttpRemainsUsableDuringBrokerOutageAndIngestionRecovers()
    {
        var options = await pg.NewDatabaseAsync(); await using var db = new SearchDbContext(options); var now = DateTimeOffset.UtcNow; var id = Guid.NewGuid(); await new ProjectionStore(new Factory(options)).ProjectAsync(new(Guid.NewGuid(), now, "trace", new(id, now, "Engineer", "Engineering", "Toronto", "Detail", 1, 2, DateOnly.FromDateTime(now.UtcDateTime).AddDays(2))), default);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" }); builder.WebHost.UseUrls("http://127.0.0.1:0"); builder.Logging.ClearProviders();
        var values = new Dictionary<string, string?> { { "SearchDatabase:ConnectionString", db.Database.GetConnectionString() }, { "Cursor:Keys:current", Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)) } };
        var settings = rabbit.Settings(); foreach (var property in typeof(ConsumerOptions).GetProperties()) values["RabbitMq:" + property.Name] = property.GetValue(settings)!.ToString(); builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System); builder.Services.AddOptions<JobSearch.Api.Configuration.SearchOptions>(); builder.Services.AddSearchPersistence(builder.Configuration); builder.Services.AddSearchMessaging(builder.Configuration); builder.Services.AddSearchReads(builder.Configuration, builder.Environment); builder.Services.AddControllers().AddApplicationPart(typeof(JobsController).Assembly);
        await using var app = builder.Build(); app.MapControllers(); await app.StartAsync(); var state = app.Services.GetRequiredService<IngestionState>(); await WaitState(state, ConsumerState.Running);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single(); using var client = new HttpClient { BaseAddress = new(address) };
        try
        {
            await rabbit.StopBrokerAsync(); await WaitState(state, ConsumerState.Backoff);
            foreach (var path in new[] { "/api/jobs", "/api/jobs/" + id, "/health/ready", "/health/live" }) Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ingestion")).StatusCode);
        }
        finally { await rabbit.StartBrokerAsync(); }
        await WaitState(state, ConsumerState.Running); Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/ingestion")).StatusCode); await app.StopAsync();
    }
    [Fact]
    public async Task BlockedReadHonorsCallerCancellationAndDatabaseCommandTimeout()
    {
        var options = await pg.NewDatabaseAsync(); await using var blocker = new SearchDbContext(options); await using var tx = await blocker.Database.BeginTransactionAsync(); await blocker.Database.ExecuteSqlRawAsync("LOCK TABLE jobs IN ACCESS EXCLUSIVE MODE");
        var query = new SearchQuery(null, null, null, 20, "newest", null); var snapshot = new CursorCodec(Microsoft.Extensions.Options.Options.Create(new CursorOptions())).Start(query, DateTimeOffset.UtcNow, 1);
        var store = new JobReadStore(new Factory(options)); using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ListAsync(query, snapshot, false, cancel.Token));
        var bounded = SearchPersistence.CreateOptions(new() { ConnectionString = blocker.Database.GetConnectionString()!, CommandTimeoutSeconds = 1 }); var error = await Assert.ThrowsAnyAsync<Exception>(() => new JobReadStore(new Factory(bounded)).ListAsync(query, snapshot, false, default)); Assert.True(error is Npgsql.NpgsqlException or InvalidOperationException { InnerException: Npgsql.NpgsqlException }); await tx.RollbackAsync(); Assert.Empty(await store.ListAsync(query, snapshot, false, default));
    }
    private static async Task WaitState(IngestionState state, ConsumerState desired) { var deadline = DateTime.UtcNow.AddSeconds(45); while (DateTime.UtcNow < deadline) { if (state.Current == desired) return; await Task.Delay(20); } throw new TimeoutException("Consumer state did not recover."); }
    private sealed class Factory(DbContextOptions<SearchDbContext> options) : IDbContextFactory<SearchDbContext> { public SearchDbContext CreateDbContext() => new(options); }
}
