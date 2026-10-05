using JobPosting.Api.Contracts;
using JobPosting.Api.Idempotency;
using JobPosting.Api.Persistence;
using JobPosting.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace JobPosting.Api.Tests;

[Trait("Category", "Unit")]
public sealed class IdempotencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);
    private static CreateJobRequest Request(string title = " Engineer ", decimal minimum = 10m, DateOnly? closing = null) => new()
    {
        Title = title, Department = "Engineering", Location = "Toronto", Description = "Description",
        SalaryMin = minimum, SalaryMax = 100m, ClosingDate = closing ?? new(2026, 10, 6)
    };
    private static PersistedPosting Saved()
    {
        var pending = PendingPosting.Create(new JobRequestValidator().Normalize(Request()).Request!,
            PostingCoordinator.Digest("Key_1"), Guid.NewGuid(), Guid.NewGuid(), Now, "original");
        return new(pending.Job);
    }
    private static PostingCoordinator Coordinator(FakeStore store, DateTimeOffset? now = null)
    {
        var clock = new Clock(now ?? Now);
        return new(store, new(), new(clock, TimeZoneInfo.Utc), clock);
    }
    [Fact]
    public async Task InvalidHeaderAndSemanticInputDoNotConsumeKeysAndCanBeCorrected()
    {
        var store = new FakeStore();
        var coordinator = Coordinator(store);
        var header = await coordinator.ResolveAsync([], Request(), "trace", default);
        Assert.Equal(PostingOutcome.InvalidKey, header.Outcome);
        Assert.Null(header.Posting);
        Assert.Null(header.Errors);
        Assert.Equal(1, header.RetryAfterSeconds);
        var fields = await coordinator.ResolveAsync(["Key_1"], Request(" "), "trace", default);
        Assert.Equal(PostingOutcome.InvalidRequest, fields.Outcome);
        Assert.Contains("title", fields.Errors!.Keys);
        Assert.Equal(0, store.Reads);
        var expired = await coordinator.ResolveAsync(["Key_1"], Request(closing: new(2026, 10, 5)), "trace", default);
        Assert.Contains("closingDate", expired.Errors!.Keys);
        Assert.Equal(0, store.Writes);
        var created = await coordinator.ResolveAsync(["Key_1"], Request(), "trace", default);
        Assert.Equal(PostingOutcome.Created, created.Outcome);
        Assert.Equal(PostingCoordinator.Digest("Key_1"), created.Posting!.Job.IdempotencyKeyDigest);
        Assert.Equal(1, store.Writes);
        Assert.NotEqual(PostingCoordinator.Digest("key_1"), created.Posting.Job.IdempotencyKeyDigest);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiredReplayPreservesSnapshotsAndNumericEquivalenceAndPublicationState(bool published)
    {
        var saved = Saved();
        if (published) saved.Job.PublishedAt = Now;
        var store = new FakeStore { Read = () => saved };
        var result = await Coordinator(store, Now.AddYears(5)).ResolveAsync(["Key_1"], Request("Engineer", 10.00m), "new-trace", default);
        Assert.Equal(published ? PostingOutcome.Published : PostingOutcome.PublicationUnresolved, result.Outcome);
        Assert.Same(saved, result.Posting);
        Assert.Equal(saved.Job.ResponseJson, result.Posting!.Job.ResponseJson);
        Assert.Equal(0, store.Writes);
    }
    [Fact]
    public async Task ConflictsAndUnsupportedVersionsNeverWriteOrChangeSavedWork()
    {
        var saved = Saved();
        var store = new FakeStore { Read = () => saved };
        Assert.Equal(PostingOutcome.Conflict, (await Coordinator(store).ResolveAsync(["Key_1"], Request("Different"), "trace", default)).Outcome);
        saved.Job.CanonicalizationVersion = 2;
        Assert.Equal(PostingOutcome.DependencyUnavailable, (await Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default)).Outcome);
        Assert.Equal(0, store.Writes);
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UniqueRaceOrUncertainCommitUsesOneFreshReadAndNeverRecreates(bool uncertain, bool found)
    {
        var saved = Saved();
        var store = new FakeStore();
        store.Read = () => store.Reads > 1 && found ? saved : null;
        store.Failure = uncertain ? new PostingCommitUncertainException(new IOException("lost")) : Database(PostgresErrorCodes.UniqueViolation);
        var result = await Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default);
        Assert.Equal(found ? PostingOutcome.PublicationUnresolved : PostingOutcome.DependencyUnavailable, result.Outcome);
        Assert.Equal(2, store.Reads);
        Assert.Equal(1, store.Writes);
    }
    [Fact]
    public async Task DifferentPayloadRaceConvergesToConflict()
    {
        var store = new FakeStore { Failure = Database(PostgresErrorCodes.UniqueViolation) };
        store.Read = () => store.Reads > 1 ? Saved() : null;
        Assert.Equal(PostingOutcome.Conflict, (await Coordinator(store).ResolveAsync(["Key_1"], Request("Other"), "trace", default)).Outcome);
    }
    [Theory]
    [InlineData(PostgresErrorCodes.LockNotAvailable, PostingOutcome.InProgress)]
    [InlineData(PostgresErrorCodes.QueryCanceled, PostingOutcome.InProgress)]
    [InlineData(PostgresErrorCodes.ConnectionFailure, PostingOutcome.DependencyUnavailable)]
    public async Task BoundedDatabaseFailureNeverRetriesWholeRequest(string sqlState, PostingOutcome expected)
    {
        var store = new FakeStore { Failure = Database(sqlState) };
        Assert.Equal(expected, (await Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default)).Outcome);
        Assert.Equal(1, store.Writes);
    }
    [Fact]
    public async Task ReadFailureIncludingReconciliationIsDependencyUnavailable()
    {
        var store = new FakeStore { Read = () => throw new NpgsqlException("private host") };
        Assert.Equal(PostingOutcome.DependencyUnavailable, (await Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default)).Outcome);
        store.Failure = new PostingCommitUncertainException(new IOException());
        store.Read = () => store.Reads > 2 ? throw new NpgsqlException("private host") : null;
        Assert.Equal(PostingOutcome.DependencyUnavailable, (await Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default)).Outcome);
    }
    [Fact]
    public async Task CancellationAndUnexpectedFailuresPropagateWithoutRecreation()
    {
        using var cancellation = new CancellationTokenSource();
        var store = new FakeStore { Failure = new PostingCommitUncertainException(new IOException()) };
        store.OnCreate = () => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", cancellation.Token));
        Assert.Equal(1, store.Reads);
        store.OnCreate = () => { };
        store.Failure = new DbUpdateException("unexpected", new InvalidOperationException());
        await Assert.ThrowsAsync<DbUpdateException>(() => Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default));
        store.Failure = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default));
        store.Failure = new NpgsqlException("connection lost before transaction");
        Assert.Equal(PostingOutcome.DependencyUnavailable, (await Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default)).Outcome);
        store.Failure = new InvalidOperationException("Provider transient wrapper", Database(PostgresErrorCodes.LockNotAvailable));
        Assert.Equal(PostingOutcome.InProgress, (await Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default)).Outcome);
        store.Failure = new DbUpdateException("No provider cause");
        await Assert.ThrowsAsync<DbUpdateException>(() => Coordinator(store).ResolveAsync(["Key_1"], Request(), "trace", default));
    }
    [Fact]
    public void PersistenceInterfaceResolvesToScopedConcreteStore()
    {
        var services = new ServiceCollection();
        services.AddPostingPersistence(new ConfigurationBuilder().Build());
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.Same(scope.ServiceProvider.GetRequiredService<PostingWriteStore>(), scope.ServiceProvider.GetRequiredService<IPostingWriteStore>());
    }
    [Fact]
    public async Task OnlySuccessfulInsertIsCreatorAndDigestUsesExactUtf8Key()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", PostingCoordinator.Digest("abc"));
        var store = new FakeStore();
        var coordinator = Coordinator(store);
        var first = await coordinator.ResolveAsync(["Key_1"], Request(), "trace", default);
        Assert.Equal(PostingOutcome.Created, first.Outcome);
        store.Read = () => first.Posting;
        var repeated = await coordinator.ResolveAsync(["Key_1"], Request(), "retry", default);
        Assert.Equal(PostingOutcome.PublicationUnresolved, repeated.Outcome);
        Assert.Equal(first.Posting!.Job.Id, repeated.Posting!.Job.Id);
        Assert.Equal(1, store.Writes);
    }
    private static DbUpdateException Database(string state) => new("safe", new PostgresException("private", "ERROR", "ERROR", state));
    private sealed class Clock(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class FakeStore : IPostingWriteStore
    {
        public int Reads;
        public int Writes;
        public Func<PersistedPosting?> Read = () => null;
        public Exception? Failure;
        public Action OnCreate = () => { };
        public Task<PersistedPosting?> ReadAsync(string digest, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Read());
        }
        public Task<bool> MarkPublishedAsync(Guid jobId, Guid eventId, DateTimeOffset at, CancellationToken token) => throw new NotSupportedException();
        public Task<bool> DeleteUnpublishedAsync(Guid jobId, Guid eventId, CancellationToken token) => throw new NotSupportedException();
        public Task CreateAsync(PendingPosting posting, CancellationToken cancellationToken)
        {
            Writes++;
            OnCreate();
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }
}
