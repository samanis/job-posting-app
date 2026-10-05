using JobPosting.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace JobPosting.Api.Resilience;
public interface IDatabaseProbe { Task<bool> CheckAsync(CancellationToken token); }
public sealed class DatabaseProbe(IDbContextFactory<PostingDbContext> contexts) : IDatabaseProbe
{
    public async Task<bool> CheckAsync(CancellationToken token)
    { await using var context = await contexts.CreateDbContextAsync(token); return await context.Database.CanConnectAsync(token); }
}
[ApiController]
[Route("health")]
public sealed class HealthController(IDatabaseProbe database, IPublisherProbe publisher, ShutdownDrain drain) : ControllerBase
{
    [HttpGet("live")]
    public IActionResult Live() => Ok(new { status = "live" });
    [HttpGet("ready")]
    public async Task<IActionResult> Ready(CancellationToken token)
    {
        if (drain.IsStopping) return StatusCode(503, new { status = "not_ready" });
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(token);
        budget.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            if (!await database.CheckAsync(budget.Token).WaitAsync(budget.Token)) return StatusCode(503, new { status = "not_ready" });
            await publisher.ProbeAsync(budget.Token).WaitAsync(budget.Token);
            return Ok(new { status = "ready" });
        }
        catch (Exception) { return StatusCode(503, new { status = "not_ready" }); }
    }
}
