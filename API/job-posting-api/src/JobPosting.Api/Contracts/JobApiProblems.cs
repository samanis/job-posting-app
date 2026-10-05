using Microsoft.AspNetCore.Mvc;

namespace JobPosting.Api.Contracts;

public static class JobApiProblems
{
    public const string IdempotencyKeyConflict = "idempotency_key_conflict";
    public const string IdempotencyInProgress = "idempotency_in_progress";
    public const string PublicationPending = "publication_pending";
    public const string DependencyUnavailable = "dependency_unavailable";

    public static ValidationProblemDetails Validation(int status, IDictionary<string, string[]> errors, string traceId)
    {
        if (status is not (400 or 422)) throw new ArgumentOutOfRangeException(nameof(status));
        var problem = new ValidationProblemDetails(errors)
        {
            Status = status,
            Title = status == 400 ? "Invalid request." : "Validation failed.",
            Type = $"https://www.rfc-editor.org/rfc/rfc9110.html#section-15.{(status == 400 ? "5.1" : "5.21") }"
        };
        problem.Extensions["traceId"] = traceId;
        return problem;
    }

    public static ProblemDetails Failure(string code, string traceId)
    {
        var (status, title, detail) = code switch
        {
            IdempotencyKeyConflict => (409, "Idempotency key conflict.", "This key is associated with a different job posting. Resolve the existing attempt before submitting another."),
            IdempotencyInProgress => (409, "Job posting is being processed.", "Wait for Retry-After, then retry the same key and payload."),
            PublicationPending => (503, "Publication is pending.", "The job has been saved, but confirmed publication is unresolved. Wait for Retry-After, then retry the same key and payload."),
            DependencyUnavailable => (503, "A required dependency is unavailable.", "The outcome may be uncertain. Wait for Retry-After, then retry the same key and payload."),
            _ => throw new ArgumentOutOfRangeException(nameof(code))
        };
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"https://www.rfc-editor.org/rfc/rfc9110.html#section-15.{(status == 409 ? "5.10" : "6.4") }"
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = traceId;
        return problem;
    }
}
