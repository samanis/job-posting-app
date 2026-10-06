using System.Data;
using System.Data.Common;
using JobSearch.Api.Contracts;
using JobSearch.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
namespace JobSearch.Api.Tests;

[Trait("Category", "Unit")]
public sealed class PersistenceTests
{
    public static JobCreatedEvent Event() => new(Guid.NewGuid(), DateTimeOffset.Parse("2026-01-01T00:00:00.1234567Z"), "trace",
        new(Guid.NewGuid(), DateTimeOffset.Parse("2026-01-01T00:00:00.1234567Z"), "Engineer", "Engineering", "Toronto", "Plain text", 0m, 999999999.99m, new(2026, 1, 2)));
    [Fact]
    public void FullSourcePrecisionAndModelArePreserved()
    {
        var e = Event(); var row = SearchJob.FromEvent(e);
        Assert.Equal(e.Job, row.ToJob()); Assert.Equal(e.EventId, row.EventId); Assert.Equal(e.OccurredAt, row.OccurredAt); Assert.Equal(e.Job.CreatedAt, row.CreatedAt);
        Assert.Equal(e.OccurredAt.UtcTicks, row.SourceOccurredAtTicks); Assert.Equal(EventFingerprint.Compute(e), row.PayloadHash); Assert.Equal(1, row.HashVersion);
        using var db = new SearchDbContext(SearchPersistence.CreateOptions(new()));
        Assert.NotNull(db.Jobs);
        var model = db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
        var entity = Assert.Single(model.GetEntityTypes()); Assert.Equal("jobs", entity.GetTableName()); Assert.Empty(entity.GetForeignKeys());
        Assert.Equal("numeric", entity.FindProperty(nameof(SearchJob.SalaryMin))!.GetColumnType()); Assert.Equal("date", entity.FindProperty(nameof(SearchJob.ClosingDate))!.GetColumnType());
        Assert.Contains(entity.GetIndexes(), x => x.IsUnique && x.Properties[0].Name == nameof(SearchJob.EventId));
        var migrations = db.GetService<IMigrationsAssembly>();
        var m = migrations.CreateMigration(Assert.Single(migrations.Migrations).Value, db.Database.ProviderName!);
        Assert.Contains(m.UpOperations, x => x is CreateTableOperation); Assert.Contains(m.DownOperations, x => x is DropTableOperation);
        var sql = db.GetService<IMigrator>().GenerateScript(); Assert.Contains("pg_trgm", sql); Assert.Contains("gin_trgm_ops", sql); Assert.Contains("scale(salary_min)", sql);
    }
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("private-secret=invalid")]
    [InlineData("Host=localhost;Database=jobs")]
    [InlineData("Database=jobs;Username=jobsearch")]
    [InlineData("Host=localhost;Username=jobsearch")]
    public void InvalidOptionsFailSafely(string? connection)
    {
        var options = new SearchDatabaseOptions { ConnectionString = connection! }; var result = new SearchDatabaseOptionsValidator().Validate(null, options);
        Assert.True(result.Failed); Assert.DoesNotContain("private-secret", result.FailureMessage);
        Assert.Throws<OptionsValidationException>(() => SearchPersistence.CreateOptions(options));
    }
    [Theory]
    [InlineData(0, 0, 0, false)]
    [InlineData(31, 5001, 31, false)]
    [InlineData(1, 1, 1, true)]
    [InlineData(30, 5000, 30, true)]
    public void TimeoutBounds(int command, int locks, int idle, bool valid)
    {
        var o = new SearchDatabaseOptions { CommandTimeoutSeconds = command, LockTimeoutMilliseconds = locks, IdleTransactionTimeoutSeconds = idle };
        var result = new SearchDatabaseOptionsValidator().Validate(null, o); Assert.Equal(valid, result.Succeeded);
        if (valid) { using var db = new SearchDbContext(SearchPersistence.CreateOptions(o)); var c = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()); Assert.False(c.IncludeErrorDetail); Assert.Equal(command, db.Database.GetCommandTimeout()); Assert.Contains($"lock_timeout={locks}", c.Options); }
        else Assert.Equal(3, result.Failures!.Count());
    }
    [Fact]
    public async Task RegistrationAndDesignFactoryHaveNoDatabaseIo()
    {
        var services = new ServiceCollection().AddLogging(); services.AddSearchPersistence(new ConfigurationBuilder().Build());
        await using var p = services.BuildServiceProvider(); await using var scope = p.CreateAsyncScope();
        await using var db = await p.GetRequiredService<IDbContextFactory<SearchDbContext>>().CreateDbContextAsync(); Assert.Equal(10, db.Database.GetCommandTimeout()); Assert.NotNull(scope.ServiceProvider.GetRequiredService<ProjectionStore>()); Assert.Same(scope.ServiceProvider.GetRequiredService<ProjectionStore>(), scope.ServiceProvider.GetRequiredService<ISearchProjection>());
        var previous = Environment.GetEnvironmentVariable("SearchDatabase__ConnectionString");
        try
        {
            Environment.SetEnvironmentVariable("SearchDatabase__ConnectionString", null); using var defaults = new SearchDbContextFactory().CreateDbContext([]); Assert.Contains("job_search", defaults.Database.GetConnectionString());
            Environment.SetEnvironmentVariable("SearchDatabase__ConnectionString", "Host=localhost;Database=design;Username=jobsearch"); using var configured = new SearchDbContextFactory().CreateDbContext([]); Assert.Contains("design", configured.Database.GetConnectionString());
        }
        finally { Environment.SetEnvironmentVariable("SearchDatabase__ConnectionString", previous); }
    }
    [Fact]
    public async Task InsertCommitsOnlyAfterLockAllocationAndSave()
    {
        var s = new Session(); var store = new ProjectionStore(new Factory(s)); Assert.Equal(ProjectionOutcome.Inserted, await store.ProjectAsync(Event(), default));
        Assert.Equal(new[] { "begin", "lock", "find", "allocate", "add", "save", "commit", "dispose-transaction", "dispose-context" }, s.Calls); Assert.Equal(42, s.Added!.IngestionSequence);
    }
    [Theory]
    [InlineData("duplicate")]
    [InlineData("job")]
    [InlineData("event")]
    [InlineData("hash")]
    [InlineData("version")]
    [InlineData("multiple")]
    public async Task ExistingIdentityNeverOverwrites(string mode)
    {
        var e = Event(); var row = SearchJob.FromEvent(e); if (mode == "job") row.Id = Guid.NewGuid(); if (mode == "event") row.EventId = Guid.NewGuid(); if (mode == "hash") row.PayloadHash = new string('b', 64); if (mode == "version") row.HashVersion = 2;
        var s = new Session { Rows = mode == "multiple" ? [row, row] : [row] }; Assert.Equal(mode == "duplicate" ? ProjectionOutcome.Duplicate : ProjectionOutcome.Conflict, await new ProjectionStore(new Factory(s)).ProjectAsync(e, default)); Assert.DoesNotContain("save", s.Calls);
    }
    [Theory]
    [InlineData("unique")]
    [InlineData("unique-missing")]
    [InlineData("other-unique")]
    [InlineData("write")]
    [InlineData("commit-known")]
    [InlineData("commit-unknown")]
    [InlineData("commit-conflict")]
    [InlineData("commit-read-fails")]
    [InlineData("cancel")]
    [InlineData("lock")]
    public async Task FailuresNeverAuthorizeAnUnverifiedProjection(string mode)
    {
        var e = Event(); var first = new Session(); var second = new Session { Rows = [SearchJob.FromEvent(e)] };
        if (mode.StartsWith("unique") || mode == "other-unique") first.SaveFailure = new DbUpdateException("write", new PostgresException("safe", "ERROR", "ERROR", "23505", constraintName: mode == "other-unique" ? "ux_jobs_ingestion_sequence" : "pk_jobs"));
        if (mode == "write") first.SaveFailure = new IOException();
        if (mode.StartsWith("commit")) first.CommitFailure = new IOException();
        if (mode == "cancel") first.CommitFailure = new OperationCanceledException();
        if (mode == "lock") first.LockFailure = new IOException();
        if (mode is "unique-missing" or "commit-unknown" or "cancel") second.Rows = [];
        if (mode == "commit-conflict") second.Rows[0].PayloadHash = "wrong";
        if (mode == "commit-read-fails") second.FindFailure = new IOException();
        var store = new ProjectionStore(new Factory(first, second));
        if (mode is "unique" or "commit-known") Assert.Equal(ProjectionOutcome.Duplicate, await store.ProjectAsync(e, default));
        else if (mode is "commit-unknown" or "commit-conflict" or "cancel") Assert.Contains("uncertain", (await Assert.ThrowsAsync<ProjectionCommitUncertainException>(() => store.ProjectAsync(e, default))).Message);
        else await Assert.ThrowsAnyAsync<Exception>(() => store.ProjectAsync(e, default));
        Assert.Contains("dispose-transaction", first.Calls); Assert.Contains("dispose-context", first.Calls);
    }
    [Fact]
    public async Task WatermarkUsesFreshContext() => Assert.Equal(42, await new ProjectionStore(new Factory(new Session())).ReadWatermarkAsync(default));
    [Fact]
    public async Task ProviderQueriesAreTranslatedWithoutExternalIo()
    {
        var interceptor = new QueryInterceptor(); var builder = new DbContextOptionsBuilder<SearchDbContext>(SearchPersistence.CreateOptions(new())).AddInterceptors(interceptor);
        await using var db = new SearchDbContext(builder.Options);
        Assert.Empty(await db.FindIdentitiesAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        await db.LockIngestionAsync(default); Assert.Equal(5, await db.AllocateSequenceAsync(default)); Assert.Equal(0, await db.ReadWatermarkAsync(default));
        interceptor.Watermark = 5; Assert.Equal(5, await db.ReadWatermarkAsync(default));
    }
    private sealed class Session
    {
        public List<string> Calls = new(); public List<SearchJob> Rows = []; public SearchJob? Added;
        public Exception? SaveFailure, CommitFailure, LockFailure, FindFailure;
    }
    private sealed class Factory(params Session[] sessions) : IDbContextFactory<SearchDbContext>
    {
        private int index;
        public SearchDbContext CreateDbContext() => new FakeContext(sessions[index++]);
    }
    private sealed class FakeContext(Session s) : SearchDbContext(SearchPersistence.CreateOptions(new()))
    {
        public override DatabaseFacade Database => new FakeDatabase(this, s);
        public override Task LockIngestionAsync(CancellationToken t) { s.Calls.Add("lock"); return s.LockFailure is null ? Task.CompletedTask : Task.FromException(s.LockFailure); }
        public override Task<List<SearchJob>> FindIdentitiesAsync(Guid id, Guid evt, CancellationToken t) { s.Calls.Add("find"); return s.FindFailure is null ? Task.FromResult(s.Rows) : Task.FromException<List<SearchJob>>(s.FindFailure); }
        public override Task<long> AllocateSequenceAsync(CancellationToken t) { s.Calls.Add("allocate"); return Task.FromResult(42L); }
        public override Task<long> ReadWatermarkAsync(CancellationToken t) => Task.FromResult(42L);
        public override void AddRange(params object[] rows) { s.Calls.Add("add"); s.Added = (SearchJob)rows[0]; }
        public override Task<int> SaveChangesAsync(CancellationToken t = default) { s.Calls.Add("save"); return s.SaveFailure is null ? Task.FromResult(1) : Task.FromException<int>(s.SaveFailure); }
        public override ValueTask DisposeAsync() { s.Calls.Add("dispose-context"); return base.DisposeAsync(); }
    }
    private sealed class FakeDatabase(DbContext db, Session s) : DatabaseFacade(db)
    {
        public override Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken t = default) { s.Calls.Add("begin"); return Task.FromResult<IDbContextTransaction>(new Transaction(s)); }
    }
    private sealed class Transaction(Session s) : IDbContextTransaction
    {
        public Guid TransactionId => Guid.Empty; public void Commit() => throw new NotSupportedException(); public void Rollback() => throw new NotSupportedException(); public Task RollbackAsync(CancellationToken t = default) => throw new NotSupportedException();
        public void Dispose() => s.Calls.Add("dispose-transaction"); public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        public Task CommitAsync(CancellationToken t = default) { s.Calls.Add("commit"); return s.CommitFailure is null ? Task.CompletedTask : Task.FromException(s.CommitFailure); }
    }
    [Fact]
    public async Task ReadProbeRequiresCurrentSchemaAndReadableJobs()
    {
        var interceptor = new QueryInterceptor(); var options = new DbContextOptionsBuilder<SearchDbContext>(SearchPersistence.CreateOptions(new())).AddInterceptors(interceptor).Options;
        var probe = new JobSearch.Api.Diagnostics.SearchDatabaseProbe(new ReadyFactory(options));
        Assert.False(await probe.ReadyAsync(default)); interceptor.Applied = true; Assert.True(await probe.ReadyAsync(default));
    }
    private sealed class ReadyFactory(DbContextOptions<SearchDbContext> options) : IDbContextFactory<SearchDbContext> { public SearchDbContext CreateDbContext() => new(options); }
    [Fact]
    public async Task SearchSqlIsParameterizedBoundedAndProjectsNoDescriptions()
    {
        var interceptor = new QueryInterceptor(); var options = new DbContextOptionsBuilder<SearchDbContext>(SearchPersistence.CreateOptions(new())).AddInterceptors(interceptor).Options;
        var store = new JobSearch.Api.Search.JobReadStore(new ReadyFactory(options)); Assert.Equal(0, await store.WatermarkAsync(default)); Assert.Null(await store.DetailAsync(Guid.NewGuid(), default));
        await using var db = new SearchDbContext(options);
        foreach (var sort in new[] { "newest", "closing-soon" }) foreach (var continuation in new[] { false, true }) foreach (var filter in new string?[] { null, "a%_\\b'" })
                {
                    var query = new JobSearch.Api.Contracts.SearchQuery(filter, filter, filter, 2, sort, null); var snapshot = SearchReadTests.Codec().Start(query, SearchReadTests.Now, 42) with { Last = new(SearchReadTests.Now.UtcTicks, Guid.NewGuid(), new DateOnly(2026, 10, 6).DayNumber) };
                    var sql = JobSearch.Api.Search.JobReadStore.Query(db, query, snapshot, continuation).ToQueryString(); Assert.Contains("LIMIT", sql); Assert.DoesNotContain("OFFSET", sql); Assert.DoesNotContain("count(", sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("description", sql.Split("FROM")[0]);
                    if (filter is not null) { Assert.Contains("ILIKE @", sql); Assert.Contains("ESCAPE", sql); Assert.Equal("%a\\%\\_\\\\b'%", JobSearch.Api.Search.JobReadStore.Pattern(filter)); }
                    Assert.Empty(await store.ListAsync(query, snapshot, continuation, default));
                }
    }
    private sealed class QueryInterceptor : DbCommandInterceptor, IDbConnectionInterceptor
    {
        public long? Watermark; public bool Applied;
        public ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection c, ConnectionEventData d, InterceptionResult result, CancellationToken t = default) => ValueTask.FromResult(InterceptionResult.Suppress());
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<int> result, CancellationToken t = default) => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(1));
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand c, CommandEventData d, InterceptionResult<DbDataReader> result, CancellationToken t = default)
        {
            var table = new DataTable();
            if (c.CommandText.Contains("__EFMigrationsHistory")) { table.Columns.Add("Value", typeof(string)); if (Applied) table.Rows.Add("20261005224236_InitialSearch"); }
            else if (c.CommandText.Contains("nextval")) { table.Columns.Add("Value", typeof(long)); table.Rows.Add(5L); }
            else if (c.CommandText.Contains("max(")) { table.Columns.Add("Value", typeof(long)); table.Rows.Add(Watermark is null ? DBNull.Value : Watermark.Value); }
            else foreach (var name in new[] { "id", "closing_date", "created_at", "department", "description", "event_id", "hash_version", "ingestion_sequence", "location", "occurred_at", "payload_hash", "salary_max", "salary_min", "source_created_at_ticks", "source_occurred_at_ticks", "title" }) table.Columns.Add(name, typeof(object));
            return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(table.CreateDataReader()));
        }
    }
}
