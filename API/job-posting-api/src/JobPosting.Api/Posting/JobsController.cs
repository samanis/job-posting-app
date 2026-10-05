using System.Text;
using JobPosting.Api.Configuration;
using JobPosting.Api.Contracts;
using JobPosting.Api.Idempotency;
using JobPosting.Api.Validation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace JobPosting.Api.Posting;

[ApiController]
[Route("api/jobs")]
public sealed class JobsController(CreateJobRequestReader reader, PostingWorkflow workflow,
    IOptions<JobPostingOptions> settings) : ControllerBase
{
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Create(CancellationToken cancellationToken)
    {
        var buffer = new byte[checked((int)settings.Value.MaximumRequestBodyBytes + 1)];
        var length = await Request.Body.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken);
        if (length == buffer.Length)
            return ProblemResult(new ProblemDetails { Status = 413, Title = "The request body is too large." });
        string json;
        try { json = new UTF8Encoding(false, true).GetString(buffer, 0, length); }
        catch (DecoderFallbackException)
        {
            return ProblemResult(JobApiProblems.Validation(400, new Dictionary<string, string[]> { ["$"] = ["The body must contain valid UTF-8 JSON."] }, HttpContext.TraceIdentifier));
        }
        var parsed = reader.Read(json);
        if (parsed.ErrorStatus.HasValue)
            return ProblemResult(JobApiProblems.Validation(parsed.ErrorStatus.Value, parsed.Errors, HttpContext.TraceIdentifier));
        var result = await workflow.SubmitAsync(Request.Headers["Idempotency-Key"].ToArray(), parsed.Request!, HttpContext.TraceIdentifier, cancellationToken);
        if (result.Outcome == PostingOutcome.Published)
            return new ContentResult { StatusCode = 202, ContentType = "application/json", Content = result.Posting!.Job.ResponseJson };
        if (result.Outcome == PostingOutcome.InvalidKey)
            return ProblemResult(JobApiProblems.Validation(400, new Dictionary<string, string[]> { ["Idempotency-Key"] = ["Supply one valid Idempotency-Key header."] }, HttpContext.TraceIdentifier));
        if (result.Outcome == PostingOutcome.InvalidRequest)
            return ProblemResult(JobApiProblems.Validation(422, result.Errors!, HttpContext.TraceIdentifier));
        var code = result.Outcome switch
        {
            PostingOutcome.Conflict => JobApiProblems.IdempotencyKeyConflict,
            PostingOutcome.InProgress => JobApiProblems.IdempotencyInProgress,
            PostingOutcome.PublicationUnresolved => JobApiProblems.PublicationUnresolved,
            _ => JobApiProblems.DependencyUnavailable
        };
        if (result.Outcome != PostingOutcome.Conflict) Response.Headers.RetryAfter = "1";
        return ProblemResult(JobApiProblems.Failure(code, HttpContext.TraceIdentifier));
    }
    private ObjectResult ProblemResult(ProblemDetails problem)
    {
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;
        return new ObjectResult(problem) { StatusCode = problem.Status, ContentTypes = { "application/problem+json" } };
    }
}
