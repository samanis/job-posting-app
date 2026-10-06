using JobSearch.Api.Contracts;
using JobSearch.Api.Persistence;
using JobSearch.Api.Search;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace JobSearch.Api.IntegrationTests;
[Trait("Category","Integration")]
public sealed class SearchIntegrationTests(PostgresFixture fixture):IClassFixture<PostgresFixture>
{
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-10-05T23:59:59.999Z");
    private static CursorCodec Codec()=>new(Options.Create(new CursorOptions{Keys=new(){{"current",Convert.ToBase64String(new byte[32])}}}));
    private static JobCreatedEvent Event(int id,int closing=7)=>new(Guid.NewGuid(),Now,"trace",new(Guid.Parse($"00000000-0000-0000-0000-{id:000000000000}"),Now.AddTicks(7),"Engineer 100%_\\ SQL'","Engineering","Toronto","Description needle",1,2,new(2026,10,closing)));
    [Fact]
    public async Task LiteralAndFiltersClosedDetailAndEmptyResultsUseSearchOnly()
    {
        var o=await fixture.NewDatabaseAsync();var factory=new Factory(o);var writer=new ProjectionStore(factory);await writer.ProjectAsync(Event(1),default);await writer.ProjectAsync(Event(2,5),default);await writer.ProjectAsync(Event(3) with{Job=Event(3).Job with{Title="100anything",Description="other",Department="Finance"}},default);
        var store=new JobReadStore(factory);var query=new SearchQuery("%_\\","engine","RON",20,"newest",null);var snapshot=Codec().Start(query,Now,await store.WatermarkAsync(default));var rows=await store.ListAsync(query,snapshot,false,default);Assert.Equal(Event(1).Job.Id,Assert.Single(rows).Id);Assert.Equal(Now.UtcTicks+7,rows[0].Summary().CreatedAt.UtcTicks);
        query=query with{Q="NEEDLE"};Assert.Single(await store.ListAsync(query,snapshot,false,default));query=query with{Q="missing"};Assert.Empty(await store.ListAsync(query,snapshot,false,default));
        var detail=await store.DetailAsync(Event(2).Job.Id,default);Assert.NotNull(detail);Assert.Equal("Description needle",detail.Description);Assert.Null(await store.DetailAsync(Guid.NewGuid(),default));
        Assert.Equal("%a\\%\\_\\\\b%",JobReadStore.Pattern("a%_\\b"));
    }
    [Theory][InlineData("newest")][InlineData("closing-soon")]
    public async Task KeysetTiesExcludeNewIngestionAndKeepUtcDateAcrossMidnight(string sort)
    {
        var o=await fixture.NewDatabaseAsync();var factory=new Factory(o);var writer=new ProjectionStore(factory);
        for(var id=1;id<=7;id++)await writer.ProjectAsync(Event(id,id%2==0?6:7),default);
        var store=new JobReadStore(factory);var query=new SearchQuery(null,null,null,2,sort,null);var codec=Codec();var snapshot=codec.Start(query,Now,await store.WatermarkAsync(default));var seen=new List<Guid>();var continuation=false;
        while(true)
        {
            var rows=await store.ListAsync(query,snapshot,continuation,default);seen.AddRange(rows.Take(query.Limit).Select(x=>x.Id));
            if(!continuation)await writer.ProjectAsync(Event(8,6),default);
            if(rows.Count<=query.Limit)break;
            snapshot=codec.Decode(codec.Encode(snapshot with{Last=rows[query.Limit-1].Position()}),query,Now.AddMinutes(1)).Payload!;continuation=true;
        }
        Assert.Equal(7,seen.Count);Assert.Equal(7,seen.Distinct().Count());Assert.DoesNotContain(Event(8).Job.Id,seen);
        var expected=sort=="newest"?new[]{7,6,5,4,3,2,1}:new[]{6,4,2,7,5,3,1};Assert.Equal(expected.Select(x=>Event(x).Job.Id),seen);
        query=query with{Limit=20};var fresh=codec.Start(query,Now.AddMinutes(1),await store.WatermarkAsync(default));Assert.Equal(4,(await store.ListAsync(query,fresh,false,default)).Count);
    }
    [Fact]
    public async Task WatermarkExcludesUncommittedAllocationAndFollowingWriterCannotOvertake()
    {
        var o=await fixture.NewDatabaseAsync();var factory=new Factory(o);await new ProjectionStore(factory).ProjectAsync(Event(1),default);
        await using var pending=new SearchDbContext(o);await using var transaction=await pending.Database.BeginTransactionAsync();await pending.LockIngestionAsync(default);var row=SearchJob.FromEvent(Event(2));row.IngestionSequence=await pending.AllocateSequenceAsync(default);pending.Add(row);await pending.SaveChangesAsync();
        var later=new ProjectionStore(factory).ProjectAsync(Event(3),default);await Task.Delay(100);Assert.False(later.IsCompleted);
        var store=new JobReadStore(factory);var query=new SearchQuery(null,null,null,20,"newest",null);var snapshot=Codec().Start(query,Now,await store.WatermarkAsync(default));Assert.Equal(1,snapshot.Watermark);await transaction.CommitAsync();await later;
        Assert.Equal(Event(1).Job.Id,Assert.Single(await store.ListAsync(query,snapshot,false,default)).Id);Assert.Equal(3,await store.WatermarkAsync(default));
    }
    private sealed class Factory(DbContextOptions<SearchDbContext> o):IDbContextFactory<SearchDbContext>{public SearchDbContext CreateDbContext()=>new(o);}
}
