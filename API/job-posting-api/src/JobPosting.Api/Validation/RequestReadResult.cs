using JobPosting.Api.Contracts;

namespace JobPosting.Api.Validation;

public sealed class RequestReadResult(CreateJobRequest? request, int? errorStatus, IDictionary<string, string[]> errors)
{
    public CreateJobRequest? Request { get; } = request;
    public int? ErrorStatus { get; } = errorStatus;
    public IDictionary<string, string[]> Errors { get; } = errors;
}
