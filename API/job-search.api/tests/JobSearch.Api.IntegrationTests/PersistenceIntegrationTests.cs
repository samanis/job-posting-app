using System.Data.Common;
using JobSearch.Api.Contracts;
using JobSearch.Api.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Npgsql;
namespace JobSearch.Api.IntegrationTests;
[Trait("Category","Integration")]
public sealed class PersistenceIntegrationTests(PostgresFixture fixture):IClassFixture<PostgresFixture>
{
    private static JobCreatedEvent Event() => new(Guid.NewGuid(),DateTimeOffset.Parse("2026-01-01T00:00:00.1234567Z"),"trace",new(Guid.NewGuid(),DateTimeOffset.Parse("2026-01-01T00:00:00.1234567Z"),"Engineer","Engineering","Toronto","Plain text",0,999999999.99m,new(2026,1,2)));
    private static ProjectionStore Store(DbContextOptions<SearchDbContext> options)=>new(new Factory(options));
    [Fact]
    public async Task FreshMigrationCompleteRoundtripIndexesAndExactReplay()
    {
        var o=await fixture.NewDatabaseAsync();var e=Event();var store=Store(o);
        Assert.Equal(0,await store.ReadWatermarkAsync(default));Assert.Equal(ProjectionOutcome.Inserted,await store.ProjectAsync(e,default));Assert.Equal(ProjectionOutcome.Duplicate,await Store(o).ProjectAsync(e,default));
        await using var db=new SearchDbContext(o);var row=await db.Jobs.SingleAsync();Assert.Equal(e.Job,row.ToJob());Assert.Equal(e.OccurredAt.UtcTicks,row.SourceOccurredAtTicks);Assert.Equal(1,row.IngestionSequence);Assert.Equal(EventFingerprint.Compute(e),row.PayloadHash);Assert.False(db.Database.HasPendingModelChanges());
        await using var c=new NpgsqlConnection(db.Database.GetConnectionString());await c.OpenAsync();
        await using var command=new NpgsqlCommand("SELECT count(*) FROM pg_extension WHERE extname='pg_trgm'",c);Assert.Equal(1L,await command.ExecuteScalarAsync());
        command.CommandText="SELECT indexdef FROM pg_indexes WHERE tablename='jobs'";await using var reader=await command.ExecuteReaderAsync();var definitions=new List<string>();while(await reader.ReadAsync())definitions.Add(reader.GetString(0));Assert.Equal(4,definitions.Count(x=>x.Contains("gin_trgm_ops")));Assert.Contains(definitions,x=>x.Contains("created_at DESC, id DESC"));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task IndependentProvidersRaceOneInsertAndClassifyConflicts(bool conflict)
    {
        var o=await fixture.NewDatabaseAsync();var e=Event();
        var providers=Enumerable.Range(0,8).Select(_=>new ServiceCollection().AddLogging().AddSingleton<IDbContextFactory<SearchDbContext>>(new Factory(o)).AddScoped<ProjectionStore>().BuildServiceProvider()).ToArray();
        try{
            var outcomes=await Task.WhenAll(providers.Select(async (p,i)=>{await using var scope=p.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ProjectionStore>().ProjectAsync(conflict&&i%2==1?e with {Job=e.Job with {Title="Different"}}:e,default);}));
            Assert.Single(outcomes,x=>x==ProjectionOutcome.Inserted);Assert.Equal(conflict?4:7,outcomes.Count(x=>x==(conflict?ProjectionOutcome.Conflict:ProjectionOutcome.Duplicate)));
            await using var db=new SearchDbContext(o);Assert.Single(await db.Jobs.ToListAsync());
        }finally{foreach(var p in providers)await p.DisposeAsync();}
    }
    [Fact]
    public async Task CrossedIdentitiesConflictButIdenticalContentDifferentJobsIsAllowed()
    {
        var o=await fixture.NewDatabaseAsync();var e=Event();var store=Store(o);await store.ProjectAsync(e,default);
        Assert.Equal(ProjectionOutcome.Conflict,await store.ProjectAsync(e with{EventId=Guid.NewGuid()},default));
        Assert.Equal(ProjectionOutcome.Conflict,await store.ProjectAsync(e with{Job=e.Job with{Id=Guid.NewGuid()}},default));
        Assert.Equal(ProjectionOutcome.Inserted,await store.ProjectAsync(e with{EventId=Guid.NewGuid(),Job=e.Job with{Id=Guid.NewGuid()}},default));
        await using var db=new SearchDbContext(o);Assert.Equal(2,await db.Jobs.CountAsync());
    }
    [Fact]
    public async Task FailedWriteRollsBackAndLostCommitAcknowledgmentReconcilesDurableRow()
    {
        var o=await fixture.NewDatabaseAsync();var e=Event();
        await Assert.ThrowsAsync<DbUpdateException>(()=>Store(o).ProjectAsync(e with{Job=e.Job with{SalaryMin=1.001m}},default));
        await using(var db=new SearchDbContext(o))Assert.Empty(await db.Jobs.ToListAsync());
        var interceptor=new LostCommit();var intercepted=new DbContextOptionsBuilder<SearchDbContext>(o).AddInterceptors(interceptor).Options;
        Assert.Equal(ProjectionOutcome.Duplicate,await Store(intercepted).ProjectAsync(e,default));Assert.True(interceptor.Lost);
        await using(var db=new SearchDbContext(o)){Assert.Single(await db.Jobs.ToListAsync());Assert.True((await Store(o).ReadWatermarkAsync(default))>1);}
    }
    [Theory][InlineData("text")][InlineData("date")][InlineData("hash")][InlineData("empty-id")][InlineData("salary")]
    public async Task DatabaseConstraintsProtectProjection(string mode)
    {
        var o=await fixture.NewDatabaseAsync();var row=SearchJob.FromEvent(Event());row.IngestionSequence=1;
        if(mode=="text")row.Title=" ";if(mode=="date")row.ClosingDate=default;if(mode=="hash")row.PayloadHash="invalid";if(mode=="empty-id")row.Id=Guid.Empty;if(mode=="salary")row.SalaryMax=1000000000;
        // DateOnly.MinValue is valid year0001, so explicitly test database infinity instead.
        await using var db=new SearchDbContext(o);
        if(mode=="date")await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlRawAsync("INSERT INTO jobs (id,event_id,payload_hash,hash_version,ingestion_sequence,occurred_at,created_at,source_occurred_at_ticks,source_created_at_ticks,title,department,location,description,salary_min,salary_max,closing_date) VALUES (gen_random_uuid(),gen_random_uuid(),repeat('a',64),1,1,now(),now(),1,1,'t','d','l','d',0,1,'infinity')"));
        else{db.Add(row);await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());}
        Assert.Empty(await db.Jobs.AsNoTracking().ToListAsync());
    }
    [Fact]
    public async Task UncommittedInsertBlocksLaterAllocationAndWatermarkExcludesBothUntilCommit()
    {
        var o=await fixture.NewDatabaseAsync();var first=Event();var second=Event();var gate=new SaveGate();var gated=new DbContextOptionsBuilder<SearchDbContext>(o).AddInterceptors(gate).Options;
        var firstTask=Store(gated).ProjectAsync(first,default);await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var later=Store(o).ProjectAsync(second,default);
        Assert.Equal(0,await Store(o).ReadWatermarkAsync(default));
        // Verify lock wait deterministically through PostgreSQL activity, rather than scheduling assumptions.
        await using var c=new NpgsqlConnection(new SearchDbContext(o).Database.GetConnectionString());await c.OpenAsync();var deadline=DateTime.UtcNow.AddSeconds(5);bool waiting=false;
        while(DateTime.UtcNow<deadline){await using var cmd=new NpgsqlCommand("SELECT EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname=current_database() AND wait_event='advisory')",c);waiting=(bool)(await cmd.ExecuteScalarAsync())!;if(waiting)break;await Task.Delay(25);}
        Assert.True(waiting);Assert.False(later.IsCompleted);gate.Release.TrySetResult();Assert.Equal(ProjectionOutcome.Inserted,await firstTask);Assert.Equal(ProjectionOutcome.Inserted,await later);
        await using var db=new SearchDbContext(o);var rows=await db.Jobs.OrderBy(x=>x.IngestionSequence).ToListAsync();Assert.Equal(first.Job.Id,rows[0].Id);Assert.Equal(second.Job.Id,rows[1].Id);var watermark=rows[0].IngestionSequence;Assert.Single(await db.Jobs.Where(x=>x.IngestionSequence<=watermark).ToListAsync());
    }
    [Fact]
    public async Task AdvisoryLockWaitIsBoundedAndCancelDoesNotInsert()
    {
        var o=await fixture.NewDatabaseAsync();await using var owner=new SearchDbContext(o);await using var transaction=await owner.Database.BeginTransactionAsync();await owner.LockIngestionAsync(default);
        var settings=new SearchDatabaseOptions{ConnectionString=owner.Database.GetConnectionString()!,LockTimeoutMilliseconds=100};
        var error=await Assert.ThrowsAsync<PostgresException>(()=>Store(SearchPersistence.CreateOptions(settings)).ProjectAsync(Event(),default));Assert.Equal("55P03",error.SqlState);
        using var canceled=new CancellationTokenSource();canceled.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Store(o).ProjectAsync(Event(),canceled.Token));
        Assert.Equal(0,await owner.Jobs.CountAsync());
    }
    private sealed class Factory(DbContextOptions<SearchDbContext> options):IDbContextFactory<SearchDbContext>{public SearchDbContext CreateDbContext()=>new(options);}
    private sealed class LostCommit:DbTransactionInterceptor
    {
        public bool Lost;
        public override Task TransactionCommittedAsync(DbTransaction t,TransactionEndEventData d,CancellationToken token=default){if(!Lost){Lost=true;throw new IOException("test-only lost commit acknowledgment");}return Task.CompletedTask;}
    }
    private sealed class SaveGate:SaveChangesInterceptor
    {
        public TaskCompletionSource Entered=new(TaskCreationOptions.RunContinuationsAsynchronously);public TaskCompletionSource Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData d,int result,CancellationToken token=default){Entered.TrySetResult();await Release.Task.WaitAsync(token);return result;}
    }
}
