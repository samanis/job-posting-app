using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using JobSearch.Api.Persistence;
using JobSearch.Api.Messaging;
namespace JobSearch.Api.Diagnostics;
public interface IDatabaseProbe { Task<bool> ReadyAsync(CancellationToken token); }
public sealed class SearchDatabaseProbe(IDbContextFactory<SearchDbContext> factory):IDatabaseProbe
{
    public async Task<bool> ReadyAsync(CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);
        var applied=await db.Database.SqlQueryRaw<string>("SELECT \"MigrationId\" AS \"Value\" FROM \"__EFMigrationsHistory\" ORDER BY \"MigrationId\"").ToArrayAsync(token);
        if(!db.Database.GetMigrations().Order().SequenceEqual(applied))return false;
        await db.Jobs.AsNoTracking().Select(x=>x.Id).Take(1).ToArrayAsync(token);
        return true;
    }
}
[ApiController]
[Route("health")]
public sealed class HealthController(IDatabaseProbe database,IngestionState ingestion,TimeProvider clock,ILogger<HealthController> logger):ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live()=>Ok(new {status="live"});
    [HttpGet("ingestion")]
    public IActionResult Ingestion()=>StatusCode(ingestion.Current==ConsumerState.Running?200:503,new {status=ingestion.Current.ToString()});
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken token)
    {
        if(ingestion.Current is ConsumerState.Draining or ConsumerState.Stopped)return StatusCode(503);
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(3),clock);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,budget.Token);
        try { return StatusCode(await database.ReadyAsync(linked.Token).WaitAsync(linked.Token)?200:503); }
        catch(Exception ex) { logger.LogWarning(new EventId(4301,"ReadUnavailable"),"Read readiness unavailable; failure {Failure}",ex.GetType().Name);return StatusCode(503); }
    }
}
