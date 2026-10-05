using System.Text.Json;
using JobPosting.Api.Contracts;
using JobPosting.Api.Persistence;
using JobPosting.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
namespace JobPosting.Api.IntegrationTests;
[Trait("Category", "Integration")]
public sealed class PostgresPersistenceTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private const string Initial = "20261005172328_InitialPosting";
    [Fact]
    public async Task EmptyDatabaseContainsOnlyJobsAndRoundTripsCompleteSnapshots()
    {
        var options = await postgres.NewDatabaseAsync();
        var store = new PostingWriteStore(new Factory(options));
        var posting = Posting(); await store.CreateAsync(posting, default);
        await using var context = new PostingDbContext(options);
        Assert.Equal(JsonSerializer.Serialize(posting.Job), JsonSerializer.Serialize(await context.Jobs.SingleAsync()));
        Assert.Equal(posting.Job.ResponseJson, (await store.ReadAsync(posting.Job.IdempotencyKeyDigest, default))!.Job.ResponseJson);
        Assert.Null(await store.ReadAsync(new string('b', 64), default));
        Assert.Equal(2, (await context.Database.GetAppliedMigrationsAsync()).Count());
        var connection = (NpgsqlConnection)context.Database.GetDbConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM information_schema.tables WHERE table_schema='public' AND table_name IN ('jobs','idempotency_records','outbox_messages')", connection);
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }
    [Theory]
    [InlineData("-0.01", "100")]
    [InlineData("1.001", "100")]
    [InlineData("1.000", "100")]
    [InlineData("0", "100.001")]
    [InlineData("0", "1000000000")]
    [InlineData("100", "100")]
    [InlineData("101", "100")]
    public async Task SalaryConstraintsRejectWithoutRounding(string minimum, string maximum)
    {
        var options = await postgres.NewDatabaseAsync(); var posting = Posting();
        posting.Job.SalaryMin = decimal.Parse(minimum, System.Globalization.CultureInfo.InvariantCulture);
        posting.Job.SalaryMax = decimal.Parse(maximum, System.Globalization.CultureInfo.InvariantCulture);
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => new PostingWriteStore(new Factory(options)).CreateAsync(posting, default));
        Assert.Equal("ck_jobs_salary", Assert.IsType<PostgresException>(error.InnerException).ConstraintName);
        await using var context = new PostingDbContext(options); Assert.Empty(await context.Jobs.ToListAsync());
    }
    [Fact]
    public async Task TextDateAndDigestConstraintsRemainEnforced()
    {
        var options = await postgres.NewDatabaseAsync(); var store = new PostingWriteStore(new Factory(options));
        foreach (var title in new[] { " ", new string('a', 201), null })
        { var bad = Posting(); bad.Job.Title = title!; await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateAsync(bad, default)); }
        foreach (var date in new[] { DateOnly.MinValue, DateOnly.MaxValue })
        { var job = Posting(); job.Job.ClosingDate = date; await store.CreateAsync(job, default); Assert.Equal(date, (await store.ReadAsync(job.Job.IdempotencyKeyDigest, default))!.Job.ClosingDate); }
        var invalid = Posting(); invalid.Job.IdempotencyKeyDigest = "raw";
        await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateAsync(invalid, default));
        await using var context = new PostingDbContext(options);
        await Assert.ThrowsAsync<PostgresException>(() => context.Database.ExecuteSqlRawAsync("UPDATE jobs SET closing_date='infinity'::date"));
        Assert.Equal(2, await context.Jobs.CountAsync());
    }
    [Fact]
    public async Task DuplicateInsertRollsBackAndExactIdentityHelpersProtectReplacementAndPublishedRows()
    {
        var options = await postgres.NewDatabaseAsync(); var store = new PostingWriteStore(new Factory(options));
        var original = Posting(); await store.CreateAsync(original, default);
        var replacement = Posting(original.Job.IdempotencyKeyDigest);
        await Assert.ThrowsAsync<DbUpdateException>(() => store.CreateAsync(replacement, default));
        Assert.False(await store.DeleteUnpublishedAsync(original.Job.Id, Guid.NewGuid(), default));
        Assert.True(await store.DeleteUnpublishedAsync(original.Job.Id, original.Job.EventId, default));
        await store.CreateAsync(replacement, default);
        Assert.False(await store.DeleteUnpublishedAsync(original.Job.Id, original.Job.EventId, default));
        var time = replacement.Job.CreatedAt.AddSeconds(1).AddTicks(9);
        Assert.True(await store.MarkPublishedAsync(replacement.Job.Id, replacement.Job.EventId, time, default));
        var recorded = (await store.ReadAsync(replacement.Job.IdempotencyKeyDigest, default))!.Job.PublishedAt;
        Assert.Equal(0, recorded!.Value.Ticks % TimeSpan.TicksPerMicrosecond);
        Assert.True(await store.MarkPublishedAsync(replacement.Job.Id, replacement.Job.EventId, time.AddDays(1), default));
        Assert.Equal(recorded, (await store.ReadAsync(replacement.Job.IdempotencyKeyDigest, default))!.Job.PublishedAt);
        Assert.False(await store.DeleteUnpublishedAsync(replacement.Job.Id, replacement.Job.EventId, default));
        Assert.False(await store.MarkPublishedAsync(Guid.NewGuid(), replacement.Job.EventId, time, default));
    }
    [Theory]
    [InlineData("fingerprint")]
    [InlineData("version")]
    [InlineData("event")]
    [InlineData("response")]
    [InlineData("publication")]
    public async Task InvalidInternalMetadataCannotBecomeASavedJob(string field)
    {
        var options = await postgres.NewDatabaseAsync();
        var posting = Posting();
        switch (field)
        {
            case "fingerprint": posting.Job.RequestFingerprint = "invalid"; break;
            case "version": posting.Job.CanonicalizationVersion = 0; break;
            case "event": posting.Job.EventId = Guid.Empty; break;
            case "response": posting.Job.ResponseJson = "{}"; break;
            case "publication": posting.Job.PublishedAt = posting.Job.CreatedAt.AddSeconds(-1); break;
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => new PostingWriteStore(new Factory(options)).CreateAsync(posting, default));
        await using var context = new PostingDbContext(options);
        Assert.Empty(await context.Jobs.ToListAsync());
    }
    [Fact]
    public async Task UpgradeTransfersPendingAndPublishedMetadataWithoutChangingSnapshots()
    {
        var options = await postgres.NewDatabaseAsync(Initial); await using var context = new PostingDbContext(options);
        var pending = Posting().Job; var published = Posting().Job;
        await SeedOldAsync(context, pending, false); await SeedOldAsync(context, published, true);
        await context.Database.MigrateAsync(); var store = new PostingWriteStore(new Factory(options));
        foreach (var row in new[] { pending, published })
            Assert.Equal(JsonSerializer.Serialize(row), JsonSerializer.Serialize((await store.ReadAsync(row.IdempotencyKeyDigest, default))!.Job));
        Assert.Equal(2, await context.Jobs.CountAsync());
    }
    [Fact]
    public async Task IncompleteOldMetadataAbortsUpgradeAndPreservesHistoryAndRow()
    {
        var options = await postgres.NewDatabaseAsync(Initial); await using var context = new PostingDbContext(options);
        await SeedJobAsync(context, Posting().Job);
        var error = await Assert.ThrowsAsync<PostgresException>(() => context.Database.MigrateAsync());
        Assert.Contains("Incomplete posting metadata", error.MessageText);
        Assert.Single(await context.Database.GetAppliedMigrationsAsync());
        var connection = (NpgsqlConnection)context.Database.GetDbConnection(); await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count(*) FROM jobs", connection); Assert.Equal(1L, await command.ExecuteScalarAsync());
    }
    private static Task<int> SeedJobAsync(PostingDbContext context, JobPostingEntity j) => context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO jobs (id,created_at,title,department,location,description,salary_min,salary_max,closing_date) VALUES ({j.Id},{j.CreatedAt},{j.Title},{j.Department},{j.Location},{j.Description},{j.SalaryMin},{j.SalaryMax},{j.ClosingDate})");
    private static async Task SeedOldAsync(PostingDbContext context, JobPostingEntity j, bool published)
    {
        await SeedJobAsync(context, j); j.PublishedAt = published ? j.CreatedAt.AddSeconds(1) : null;
        var envelope = JsonSerializer.Serialize(new { eventId=j.EventId, eventType="JobPostingCreated", schemaVersion=1, job=new { id=j.Id } });
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO outbox_messages (event_id,job_id,event_type,schema_version,occurred_at,envelope_json,claim_generation,attempt_count,next_attempt_at,published_at) VALUES ({j.EventId},{j.Id},'JobPostingCreated',1,{j.CreatedAt},{envelope},0,0,{j.CreatedAt},{j.PublishedAt})");
        await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO idempotency_records (scope,key_digest,canonicalization_version,fingerprint,job_id,event_id,created_at,response_json) VALUES ('create-job',{j.IdempotencyKeyDigest},{j.CanonicalizationVersion},{j.RequestFingerprint},{j.Id},{j.EventId},{j.CreatedAt},{j.ResponseJson})");
    }
    private static PendingPosting Posting(string? digest = null)
    {
        var request = new JobRequestValidator().Normalize(new CreateJobRequest { Title="Engineer", Department="Engineering", Location="Toronto", Description="Plain text", SalaryMin=0m, SalaryMax=999999999.99m, ClosingDate=new(2028,2,29) }).Request!;
        return PendingPosting.Create(request,digest ?? Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),Guid.NewGuid(),Guid.NewGuid(),new DateTimeOffset(2026,10,5,14,0,0,TimeSpan.FromHours(-4)).AddTicks(1234567),"test");
    }
    private sealed class Factory(DbContextOptions<PostingDbContext> options) : IDbContextFactory<PostingDbContext>
    { public PostingDbContext CreateDbContext() => new(options); }
}
