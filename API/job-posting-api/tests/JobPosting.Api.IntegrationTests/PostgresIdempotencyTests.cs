using System.Data.Common;
using JobPosting.Api.Contracts;
using JobPosting.Api.Idempotency;
using JobPosting.Api.Persistence;
using JobPosting.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace JobPosting.Api.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class PostgresIdempotencyTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static CreateJobRequest Request(string title = "Engineer", decimal salary = 10m) => new()
    {
        Title = title, Department = "Engineering", Location = "Toronto", Description = "Plain text",
        SalaryMin = salary, SalaryMax = 100m, ClosingDate = new(2026, 10, 6)
    };
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IndependentHostsRaceThenRestartAndExpiredReplayRetainExactlyOneIdentity(bool conflicting)
    {
        var options = await postgres.NewDatabaseAsync();
        await using var connectionContext = new PostingDbContext(options);
        var connection = connectionContext.Database.GetConnectionString()!;
        var barrier = new ReadBarrier(8);
        async Task<PostingResolution> Attempt(int index)
        {
            // Independent service providers/factories/contexts model separate process hosts.
            await using var host = Host(connection, Now, barrier);
            await using var scope = host.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<PostingCoordinator>().ResolveAsync(["race-key"],
                Request(conflicting && index % 2 == 1 ? "Other" : " Engineer ", index % 2 == 0 ? 10m : 10.00m), "trace-" + index, default);
        }
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(Attempt)).WaitAsync(TimeSpan.FromSeconds(30));
        var successes = results.Where(result => result.Outcome is PostingOutcome.Created or PostingOutcome.PublicationUnresolved).ToArray();
        Assert.Equal(conflicting ? 4 : 8, successes.Length);
        Assert.Single(results, result => result.Outcome == PostingOutcome.Created);
        Assert.Equal(conflicting ? 4 : 0, results.Count(result => result.Outcome == PostingOutcome.Conflict));
        Assert.Single(successes.Select(result => result.Posting!.Job.Id).Distinct());
        Assert.Single(successes.Select(result => result.Posting!.Job.EventId).Distinct());
        Assert.Single(successes.Select(result => result.Posting!.Job.ResponseJson).Distinct());
        Assert.Single(successes.Select(result => result.Posting!.Job.ResponseJson).Distinct());
        var original = successes[0].Posting!;
        var title = (await connectionContext.Jobs.SingleAsync()).Title;
        await using var restarted = Host(connection, Now.AddYears(3));
        await using var restartedScope = restarted.CreateAsyncScope();
        var replay = await restartedScope.ServiceProvider.GetRequiredService<PostingCoordinator>()
            .ResolveAsync(["race-key"], Request(title, 10.00m), "restart-trace", default);
        Assert.Equal(PostingOutcome.PublicationUnresolved, replay.Outcome);
        Assert.Equal(original.Job.ResponseJson, replay.Posting!.Job.ResponseJson);
        Assert.Equal(original.Job.ResponseJson, replay.Posting.Job.ResponseJson);
        Assert.Equal(original.Job.CreatedAt, replay.Posting.Job.CreatedAt);
        Assert.Equal(1, await connectionContext.Jobs.CountAsync());
        // Set durable publication state as test setup only; no broker acceptance is fabricated by production.
        var store = new PostingWriteStore(new Factory(options));
        Assert.True(await store.MarkPublishedAsync(original.Job.Id, original.Job.EventId, Now.AddSeconds(1), default));
        var completed = await restartedScope.ServiceProvider.GetRequiredService<PostingCoordinator>()
            .ResolveAsync(["race-key"], Request(title, 10.00m), "completed", default);
        Assert.Equal(PostingOutcome.Published, completed.Outcome);
        Assert.Equal(original.Job.ResponseJson, completed.Posting!.Job.ResponseJson);
        Assert.Equal(1, await connectionContext.Jobs.CountAsync());
    }
    [Fact]
    public async Task InvalidNewRequestDoesNotConsumeKeyAndUnknownCommitResumesOriginal()
    {
        var options = await postgres.NewDatabaseAsync();
        var faulted = new DbContextOptionsBuilder<PostingDbContext>(options).AddInterceptors(new LoseAcknowledgement()).Options;
        var store = new PostingWriteStore(new Factory(faulted));
        var clock = new Clock(Now);
        var coordinator = new PostingCoordinator(store, new(), new(clock, TimeZoneInfo.Utc), clock);
        var invalid = await coordinator.ResolveAsync(["correctable"], Request(" "), "trace", default);
        Assert.Equal(PostingOutcome.InvalidRequest, invalid.Outcome);
        await using var verification = new PostingDbContext(options);
        Assert.Equal(0, await verification.Jobs.CountAsync());
        var saved = await coordinator.ResolveAsync(["correctable"], Request(), "trace", default);
        Assert.Equal(PostingOutcome.PublicationUnresolved, saved.Outcome);
        // The interceptor loses acknowledgment after the actual commit; coordinator rereads it.
        var restarted = new PostingCoordinator(new PostingWriteStore(new Factory(options)), new(), new(clock, TimeZoneInfo.Utc), clock);
        var replay = await restarted.ResolveAsync(["correctable"], Request(), "another", default);
        Assert.Equal(saved.Posting!.Job.Id, replay.Posting!.Job.Id);
        Assert.Equal(saved.Posting.Job.EventId, replay.Posting.Job.EventId);
        Assert.Equal(saved.Posting.Job.ResponseJson, replay.Posting.Job.ResponseJson);
        Assert.Equal(1, await verification.Jobs.CountAsync());
    }
    [Fact]
    public async Task UncommittedOwnerCausesBoundedInProgressThenSameIdentityReplayAfterCommit()
    {
        var options = await postgres.NewDatabaseAsync();
        await using var owner = new PostingDbContext(options);
        var bounded = PostingPersistence.CreateOptions(new PostingDatabaseOptions
        {
            ConnectionString = owner.Database.GetConnectionString()!, LockTimeoutMilliseconds = 200
        });
        var pending = PendingPosting.Create(new JobRequestValidator().Normalize(Request()).Request!, PostingCoordinator.Digest("held-key"),
            Guid.NewGuid(), Guid.NewGuid(), Now, "owner");
        await using var transaction = await owner.Database.BeginTransactionAsync();
        owner.AddRange(pending.Job);
        await owner.SaveChangesAsync();
        var clock = new Clock(Now);
        var coordinator = new PostingCoordinator(new PostingWriteStore(new Factory(bounded)), new(), new(clock, TimeZoneInfo.Utc), clock);
        var result = await coordinator.ResolveAsync(["held-key"], Request(), "contender", default).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(PostingOutcome.InProgress, result.Outcome);
        Assert.Equal(1, result.RetryAfterSeconds);
        await transaction.CommitAsync();
        var replay = await coordinator.ResolveAsync(["held-key"], Request(), "retry", default);
        Assert.Equal(PostingOutcome.PublicationUnresolved, replay.Outcome);
        Assert.Equal(pending.Job.Id, replay.Posting!.Job.Id);
        await using var fresh = new PostingDbContext(options);
        Assert.Equal(1, await fresh.Jobs.CountAsync());
    }
    [Fact]
    public async Task DifferentKeysAllowSameContentAndCompensationDeletionReleasesOnlyItsOwnKey()
    {
        var options = await postgres.NewDatabaseAsync();
        var store = new PostingWriteStore(new Factory(options));
        var clock = new Clock(Now);
        var coordinator = new PostingCoordinator(store, new(), new(clock, TimeZoneInfo.Utc), clock);
        var first = await coordinator.ResolveAsync(["first"], Request(), "trace", default);
        var second = await coordinator.ResolveAsync(["second"], Request(), "trace", default);
        Assert.Equal(PostingOutcome.Created, first.Outcome);
        Assert.Equal(PostingOutcome.Created, second.Outcome);
        Assert.NotEqual(first.Posting!.Job.Id, second.Posting!.Job.Id);
        Assert.Equal(first.Posting.Job.RequestFingerprint, second.Posting.Job.RequestFingerprint);
        Assert.True(await store.DeleteUnpublishedAsync(first.Posting.Job.Id, first.Posting.Job.EventId, default));
        var replacement = await coordinator.ResolveAsync(["first"], Request(), "replacement", default);
        Assert.Equal(PostingOutcome.Created, replacement.Outcome);
        Assert.NotEqual(first.Posting.Job.Id, replacement.Posting!.Job.Id);
        Assert.Equal(PostingOutcome.PublicationUnresolved, (await coordinator.ResolveAsync(["second"], Request(), "duplicate", default)).Outcome);
        await using var context = new PostingDbContext(options);
        Assert.Equal(2, await context.Jobs.CountAsync());
    }
    private static ServiceProvider Host(string connection, DateTimeOffset now, ReadBarrier? barrier = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new Clock(now));
        services.AddSingleton(TimeZoneInfo.Utc);
        services.AddSingleton<JobRequestValidator>();
        services.AddSingleton<NewJobTemporalValidator>();
        services.AddPostingPersistence(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["PostingDatabase:ConnectionString"] = connection
        }).Build());
        if (barrier is not null) services.AddScoped<IPostingWriteStore>(provider => new GatedStore(provider.GetRequiredService<PostingWriteStore>(), barrier));
        return services.BuildServiceProvider();
    }
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class Factory(DbContextOptions<PostingDbContext> options) : IDbContextFactory<PostingDbContext>
    {
        public PostingDbContext CreateDbContext() => new(options);
    }
    private sealed class LoseAcknowledgement : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
            => Task.FromException(new IOException("Lost acknowledgment after commit"));
    }
    private sealed class ReadBarrier(int count)
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task ArriveAsync()
        {
            if (Interlocked.Increment(ref arrivals) == count) ready.SetResult();
            return ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
    }
    private sealed class GatedStore(PostingWriteStore store, ReadBarrier barrier) : IPostingWriteStore
    {
        private bool first = true;
        public Task<bool> MarkPublishedAsync(Guid jobId, Guid eventId, DateTimeOffset at, CancellationToken token) => store.MarkPublishedAsync(jobId, eventId, at, token);
        public Task<bool> DeleteUnpublishedAsync(Guid jobId, Guid eventId, CancellationToken token) => store.DeleteUnpublishedAsync(jobId, eventId, token);
        public Task CreateAsync(PendingPosting posting, CancellationToken cancellationToken) => store.CreateAsync(posting, cancellationToken);
        public async Task<PersistedPosting?> ReadAsync(string digest, CancellationToken cancellationToken)
        {
            var result = await store.ReadAsync(digest, cancellationToken);
            if (first) { first = false; await barrier.ArriveAsync(); }
            return result;
        }
    }
}
