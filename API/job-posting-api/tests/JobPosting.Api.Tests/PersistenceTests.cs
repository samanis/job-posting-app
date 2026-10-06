using System.Text.Json;
using JobPosting.Api.Contracts;
using JobPosting.Api.Persistence;
using JobPosting.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace JobPosting.Api.Tests;

[Trait("Category", "Unit")]
public sealed class PersistenceTests
{
    public static PendingPosting Posting(string digest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
    {
        var normalized = new JobRequestValidator().Normalize(new CreateJobRequest
        {
            Title = "Engineer",
            Department = "Engineering",
            Location = "Toronto",
            Description = "Plain text\nDescription",
            SalaryMin = 0m,
            SalaryMax = 999999999.99m,
            ClosingDate = new DateOnly(2028, 2, 29)
        }).Request!;
        return PendingPosting.Create(normalized, digest, Guid.NewGuid(), Guid.NewGuid(),
            new DateTimeOffset(2026, 10, 5, 14, 0, 0, TimeSpan.FromHours(-4)).AddTicks(1234567), "trace-fixture");
    }

    [Fact]
    public void JobContainsCompleteStableResponseAndInternalIdempotencyMetadata()
    {
        var job = Posting().Job;
        Assert.Equal(TimeSpan.Zero, job.CreatedAt.Offset);
        Assert.Equal(0, job.CreatedAt.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.Equal(1234560, job.CreatedAt.Ticks % TimeSpan.TicksPerSecond);
        Assert.Equal(new string('a', 64), job.IdempotencyKeyDigest);
        Assert.Matches("^[a-f0-9]{64}$", job.RequestFingerprint);
        Assert.Equal(1, job.CanonicalizationVersion);
        Assert.NotEqual(Guid.Empty, job.EventId);
        Assert.Null(job.PublishedAt);
        var response = JsonSerializer.Deserialize<JobAcceptedResponse>(job.ResponseJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(job.Id.ToString("D"), response.Id);
        Assert.Equal(job.CreatedAt, response.CreatedAt);
        Assert.Equal(job.Title, response.Title);
        Assert.Equal(job.Department, response.Department);
        Assert.Equal(job.Location, response.Location);
        Assert.Equal(job.Description, response.Description);
        Assert.Equal(job.SalaryMin, response.SalaryMin);
        Assert.Equal(job.SalaryMax, response.SalaryMax);
        Assert.Equal(job.ClosingDate, response.ClosingDate);
        Assert.DoesNotContain("idempotency", job.ResponseJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("eventId", job.ResponseJson);
    }

    [Fact]
    public void ModelHasOnlyJobsAndUniqueDigestWithExactTypes()
    {
        using var context = new PostingDbContext(PostingPersistence.CreateOptions(new()));
        Assert.NotNull(context.Jobs);
        var model = context.GetService<IDesignTimeModel>().Model;
        var job = Assert.Single(model.GetEntityTypes());
        Assert.Equal("jobs", job.GetTableName());
        Assert.Equal("numeric", job.FindProperty(nameof(JobPostingEntity.SalaryMin))!.GetColumnType());
        Assert.Equal("numeric", job.FindProperty(nameof(JobPostingEntity.SalaryMax))!.GetColumnType());
        Assert.Equal("date", job.FindProperty(nameof(JobPostingEntity.ClosingDate))!.GetColumnType());
        Assert.Equal("timestamp with time zone", job.FindProperty(nameof(JobPostingEntity.CreatedAt))!.GetColumnType());
        Assert.Contains(job.GetIndexes(), index => index.IsUnique && index.Properties.Single().Name == "IdempotencyKeyDigest");
        Assert.Empty(job.GetForeignKeys());
        Assert.Contains("scale(salary_min) <= 2", job.GetCheckConstraints().Single(item => item.Name == "ck_jobs_salary").Sql);
    }

    [Fact]
    public void AllAuthoredMigrationsAndUpgradeGuardAreExecutableAndIncluded()
    {
        using var context = new PostingDbContext(PostingPersistence.CreateOptions(new()));
        var assembly = context.GetService<IMigrationsAssembly>();
        foreach (var type in assembly.Migrations.Values)
        {
            var migration = assembly.CreateMigration(type, context.Database.ProviderName!);
            Assert.NotEmpty(migration.UpOperations);
            if (type.Name == "InitialPosting") Assert.Equal(3, migration.DownOperations.OfType<DropTableOperation>().Count());
            else Assert.Throws<NotSupportedException>(() => migration.DownOperations);
        }
        var script = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE jobs", script);
        Assert.Contains("UPDATE jobs", script);
        Assert.Contains("idempotency_key_digest", script);
        Assert.Contains("Incomplete posting metadata", script);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("private-secret=not-a-valid-setting")]
    [InlineData("Host=localhost;Database=jobs")]
    [InlineData("Database=jobs;Username=jobposting")]
    [InlineData("Host=localhost;Username=jobposting")]
    public void InvalidDatabaseConfigurationDoesNotEchoSecrets(string? connection)
    {
        var settings = new PostingDatabaseOptions { ConnectionString = connection! };
        var result = new PostingDatabaseOptionsValidator().Validate(null, settings);
        Assert.True(result.Failed);
        Assert.DoesNotContain("private-secret", result.FailureMessage);
        Assert.Throws<OptionsValidationException>(() => PostingPersistence.CreateOptions(settings));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(31, 5001, 31)]
    public void InvalidDatabaseTimeoutsAreBounded(int command, int lockMilliseconds, int idle)
    {
        var result = new PostingDatabaseOptionsValidator().Validate(null, new PostingDatabaseOptions
        { CommandTimeoutSeconds = command, LockTimeoutMilliseconds = lockMilliseconds, IdleTransactionTimeoutSeconds = idle });
        Assert.True(result.Failed);
        Assert.Equal(3, result.Failures.Count());
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(30, 5000, 30)]
    public void ValidTimeoutBoundariesAreAppliedAndSensitiveDetailDisabled(int command, int lockMilliseconds, int idle)
    {
        var settings = new PostingDatabaseOptions
        {
            ConnectionString = "Host=localhost;Database=jobs;Username=jobposting;Include Error Detail=true",
            CommandTimeoutSeconds = command,
            LockTimeoutMilliseconds = lockMilliseconds,
            IdleTransactionTimeoutSeconds = idle
        };
        Assert.True(new PostingDatabaseOptionsValidator().Validate(null, settings).Succeeded);
        using var context = new PostingDbContext(PostingPersistence.CreateOptions(settings));
        var connection = new NpgsqlConnectionStringBuilder(context.Database.GetConnectionString());
        Assert.Equal(5, connection.Timeout);
        Assert.Equal(command, context.Database.GetCommandTimeout());
        Assert.False(connection.IncludeErrorDetail);
        Assert.Contains($"lock_timeout={lockMilliseconds}", connection.Options);
        Assert.Contains($"idle_in_transaction_session_timeout={idle * 1000}", connection.Options);
    }

    [Fact]
    public async Task DependencyRegistrationBuildsContextFactoryWithoutContactingDatabase()
    {
        var services = new ServiceCollection().AddLogging();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["PostingDatabase:CommandTimeoutSeconds"] = "7" }).Build();
        services.AddPostingPersistence(config);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<PostingDbContext>>();
        await using var context = await factory.CreateDbContextAsync();
        Assert.Equal(7, context.Database.GetCommandTimeout());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<PostingWriteStore>());
    }

    [Fact]
    public void DesignTimeFactoryUsesSafeDefaultsOrEnvironmentWithoutARequiredDatabase()
    {
        var previous = Environment.GetEnvironmentVariable("PostingDatabase__ConnectionString");
        try
        {
            Environment.SetEnvironmentVariable("PostingDatabase__ConnectionString", null);
            using var defaults = new PostingDbContextFactory().CreateDbContext([]);
            Assert.Contains("job_postings", defaults.Database.GetConnectionString());
            Environment.SetEnvironmentVariable("PostingDatabase__ConnectionString", "Host=localhost;Database=design_test;Username=jobposting");
            using var configured = new PostingDbContextFactory().CreateDbContext([]);
            Assert.Contains("design_test", configured.Database.GetConnectionString());
        }
        finally { Environment.SetEnvironmentVariable("PostingDatabase__ConnectionString", previous); }
    }

    [Fact]
    public async Task AtomicCreateStagesOneJobBeforeSavingAndCommitsOnce()
    {
        var session = new FakeSession();
        var factory = new FakeFactory(session);
        var posting = Posting();
        using var cancellation = new CancellationTokenSource();
        await new PostingWriteStore(factory).CreateAsync(posting, cancellation.Token);
        Assert.Equal(new[] { "create", "begin", "add", "save", "commit", "dispose-transaction", "dispose-context" }, session.Calls);
        Assert.Equal(new object[] { posting.Job }, session.Added);
        Assert.All(session.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(1, factory.Created);
    }

    [Fact]
    public async Task FailureBeforeCommitDisposesWithoutAttemptingCommitOrRetry()
    {
        var failure = new DbUpdateException("synthetic write failure");
        var session = new FakeSession { SaveFailure = failure };
        var factory = new FakeFactory(session);
        Assert.Same(failure, await Assert.ThrowsAsync<DbUpdateException>(() => new PostingWriteStore(factory).CreateAsync(Posting(), default)));
        Assert.DoesNotContain("commit", session.Calls);
        Assert.Contains("dispose-transaction", session.Calls);
        Assert.Contains("dispose-context", session.Calls);
        Assert.Equal(1, factory.Created);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostCommitAcknowledgementOrCommitCancellationIsUncertainAndReconciliationUsesFreshContext(bool cancelled)
    {
        var posting = Posting();
        Exception failure = cancelled ? new OperationCanceledException() : new IOException("synthetic lost acknowledgement");
        var attempt = new FakeSession { CommitFailure = failure };
        var persisted = new FakeSession { Job = posting.Job };
        var factory = new FakeFactory(attempt, persisted);
        var store = new PostingWriteStore(factory);
        var exception = await Assert.ThrowsAsync<PostingCommitUncertainException>(() => store.CreateAsync(posting, default));
        Assert.Same(failure, exception.InnerException);
        Assert.Contains("uncertain", exception.Message);
        Assert.Equal(1, factory.Created);
        var recovered = Assert.IsType<PersistedPosting>(await store.ReadAsync(posting.Job.IdempotencyKeyDigest, default));
        Assert.Same(posting.Job, recovered.Job);
        Assert.Equal(2, factory.Created);
        Assert.Equal(posting.Job.IdempotencyKeyDigest, persisted.DigestLookup);
    }

    [Fact]
    public async Task MissingJobReadReturnsNull()
    {
        Assert.Null(await new PostingWriteStore(new FakeFactory(new FakeSession())).ReadAsync(new string('a', 64), default));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("different-event")]
    [InlineData("published")]
    [InlineData("pending")]
    public async Task ExactIdentityPublicationAndDeletionNeverAffectReplacementOrPublishedJob(string state)
    {
        var job = state == "missing" ? null : Posting().Job;
        if (state == "published") job!.PublishedAt = job.CreatedAt;
        var session = new FakeSession { Job = job };
        var eventId = state == "different-event" ? Guid.NewGuid() : job?.EventId ?? Guid.NewGuid();
        var store = new PostingWriteStore(new FakeFactory(session, session));
        var id = job?.Id ?? Guid.NewGuid();
        var deleted = await store.DeleteUnpublishedAsync(id, eventId, default);
        Assert.Equal(state == "pending", deleted);
        Assert.Equal(state == "pending", session.Removed);
        var marked = await store.MarkPublishedAsync(id, eventId, DateTimeOffset.UtcNow, default);
        Assert.Equal(state is "pending" or "published", marked);
        if (state == "pending") Assert.NotNull(job!.PublishedAt);
        if (state == "published") Assert.DoesNotContain("save", session.Calls);
    }

    [Fact]
    public async Task PreWriteCancellationIsNotWrappedAsAnUncertainCommit()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var session = new FakeSession();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new PostingWriteStore(new FakeFactory(session)).CreateAsync(Posting(), cancellation.Token));
        Assert.Empty(session.Calls);
    }

    [Fact]
    public async Task DigestQueryFiltersExactKeyWithoutDatabaseAndHonorsCancellation()
    {
        var job = Posting().Job;
        await using var context = new QueryContext(job);
        Assert.Same(job, await context.FindByDigestAsync(job.IdempotencyKeyDigest, default));
        Assert.Null(await context.FindByDigestAsync(new string('b', 64), default));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => context.FindByDigestAsync(job.IdempotencyKeyDigest, cancellation.Token));
    }

    private sealed class QueryContext(JobPostingEntity job) : PostingDbContext(PostingPersistence.CreateOptions(new()))
    { public override DbSet<JobPostingEntity> Jobs => new QuerySet(new[] { job }.AsQueryable()); }
    private sealed class QuerySet(IQueryable<JobPostingEntity> rows) : DbSet<JobPostingEntity>, IQueryable<JobPostingEntity>
    {
        public override IEntityType EntityType => throw new NotSupportedException();
        Type IQueryable.ElementType => rows.ElementType;
        System.Linq.Expressions.Expression IQueryable.Expression => rows.Expression;
        IQueryProvider IQueryable.Provider => new AsyncProvider(rows.Provider);
    }
    private sealed class AsyncProvider(IQueryProvider inner) : Microsoft.EntityFrameworkCore.Query.IAsyncQueryProvider
    {
        public IQueryable CreateQuery(System.Linq.Expressions.Expression expression) => inner.CreateQuery(expression);
        public IQueryable<T> CreateQuery<T>(System.Linq.Expressions.Expression expression) => inner.CreateQuery<T>(expression);
        public object? Execute(System.Linq.Expressions.Expression expression) => inner.Execute(expression);
        public T Execute<T>(System.Linq.Expressions.Expression expression) => inner.Execute<T>(expression);
        public TResult ExecuteAsync<TResult>(System.Linq.Expressions.Expression expression, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = inner.Execute(expression);
            return (TResult)typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(typeof(TResult).GenericTypeArguments[0]).Invoke(null, [result])!;
        }
    }

    private sealed class FakeSession
    {
        public List<string> Calls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public object[]? Added { get; set; }
        public Exception? SaveFailure { get; init; }
        public Exception? CommitFailure { get; init; }
        public JobPostingEntity? Job { get; init; }
        public string? DigestLookup;
        public bool Removed;
    }

    private sealed class FakeFactory(params FakeSession[] sessions) : IDbContextFactory<PostingDbContext>
    {
        public int Created { get; private set; }
        public PostingDbContext CreateDbContext() => new FakeContext(sessions[Created++]);
        public Task<PostingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var context = CreateDbContext();
            sessions[Created - 1].Calls.Add("create");
            return Task.FromResult(context);
        }
    }

    private sealed class FakeContext(FakeSession session) : PostingDbContext(PostingPersistence.CreateOptions(new()))
    {
        public override DatabaseFacade Database => new FakeDatabase(this, session);
        public override Task<JobPostingEntity?> FindByDigestAsync(string digest, CancellationToken cancellationToken)
        { session.DigestLookup = digest; session.Tokens.Add(cancellationToken); return Task.FromResult(session.Job); }
        public override DbSet<JobPostingEntity> Jobs => new FakeSet<JobPostingEntity>((keys, token) => session.Job);
        public override Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Remove<TEntity>(TEntity entity)
        { session.Removed = true; return null!; }
        public override void AddRange(params object[] entities) { session.Calls.Add("add"); session.Added = entities; }
        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            session.Calls.Add("save"); session.Tokens.Add(cancellationToken);
            return session.SaveFailure is null ? Task.FromResult(3) : Task.FromException<int>(session.SaveFailure);
        }
        public override ValueTask DisposeAsync() { session.Calls.Add("dispose-context"); return base.DisposeAsync(); }
    }

    private sealed class FakeSet<TEntity>(Func<object?[]?, CancellationToken, TEntity?> find) : DbSet<TEntity> where TEntity : class
    {
        public override IEntityType EntityType => throw new NotSupportedException();
        public override ValueTask<TEntity?> FindAsync(object?[]? keyValues, CancellationToken cancellationToken) => ValueTask.FromResult(find(keyValues, cancellationToken));
    }

    private sealed class FakeDatabase(DbContext context, FakeSession session) : DatabaseFacade(context)
    {
        public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            session.Calls.Add("begin"); session.Tokens.Add(cancellationToken);
            return Task.FromResult<IDbContextTransaction>(new FakeTransaction(session));
        }
    }

    private sealed class FakeTransaction(FakeSession session) : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();
        public void Commit() => throw new NotSupportedException();
        public void Rollback() => throw new NotSupportedException();
        public Task RollbackAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public void Dispose() => session.Calls.Add("dispose-transaction");
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            session.Calls.Add("commit"); session.Tokens.Add(cancellationToken);
            return session.CommitFailure is null ? Task.CompletedTask : Task.FromException(session.CommitFailure);
        }
    }
}
