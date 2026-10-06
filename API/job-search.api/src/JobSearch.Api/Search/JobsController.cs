using JobSearch.Api.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Npgsql;
namespace JobSearch.Api.Search;
[ApiController]
[Route("api/jobs")]
public sealed class JobsController(QueryReader reader,CursorCodec cursors,IJobReadStore store,TimeProvider clock,ILogger<JobsController> logger):ControllerBase
{
    [HttpGet]
    [OutputCache(PolicyName=SearchCaching.ListPolicy)]
    [ProducesResponseType<JobPage>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> List(CancellationToken token)
    {
        var result=reader.Read(Request.Query);if(result.Query is null)return Error(400,result.Error!);
        var query=result.Query;var now=clock.GetUtcNow();CursorPayload? snapshot=null;
        if(query.Cursor is not null){var decoded=cursors.Decode(query.Cursor,query,now);if(decoded.Payload is null)return Error(decoded.Error=="cursor_expired"?409:400,decoded.Error!);snapshot=decoded.Payload;}
        try
        {
            snapshot??=cursors.Start(query,now,await store.WatermarkAsync(token));
            var rows=await store.ListAsync(query,snapshot,query.Cursor is not null,token);
            var items=rows.Take(query.Limit).ToList();
            var next=rows.Count>query.Limit?cursors.Encode(snapshot with {Last=items[^1].Position()}):null;
            Response.Headers.CacheControl=query.Cursor is null?SearchCaching.FirstPageHeader:SearchCaching.ContinuationPageHeader;
            return Ok(new JobPage(items.Select(x=>x.Summary()).ToArray(),next));
        }
        catch(Exception ex) when(ex is NpgsqlException or TimeoutException or InvalidOperationException {InnerException:NpgsqlException}){return Unavailable(ex);}
    }
    [HttpGet("{id}")]
    [OutputCache(PolicyName=SearchCaching.DetailPolicy)]
    [ProducesResponseType<JobDetail>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(404)]
    [ProducesResponseType<ProblemDetails>(503)]
    [ProducesResponseType<ProblemDetails>(500)]
    public async Task<IActionResult> Detail(string id,CancellationToken token)
    {
        if(!QueryReader.TryId(id,out var parsed))return Error(400,"invalid_id");
        try
        {
            var job=await store.DetailAsync(parsed,token);if(job is null)return Error(404,"job_not_found");
            Response.Headers.CacheControl=SearchCaching.DetailHeader;
            return Ok(job);
        }
        catch(Exception ex) when(ex is NpgsqlException or TimeoutException or InvalidOperationException {InnerException:NpgsqlException}){return Unavailable(ex);}
    }
    private IActionResult Unavailable(Exception ex)
    {logger.LogWarning(new EventId(4401,"SearchUnavailable"),"Search unavailable; failure {Failure}",ex.GetType().Name);return Error(503,"search_unavailable");}
    private ObjectResult Error(int status,string code)
    {
        // A 404 may become a 200 moments later, once the job is ingested; never cache errors.
        Response.Headers.CacheControl=SearchCaching.NoStoreHeader;
        return new(new ProblemDetails{Status=status,Title="The search request could not be completed.",Extensions={{"code",code},{"traceId",HttpContext.TraceIdentifier}}}){StatusCode=status,ContentTypes={"application/problem+json"}};
    }
}
