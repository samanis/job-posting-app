using JobPosting.Api.Contracts;

namespace JobPosting.Api.Validation;

public sealed class JobRequestValidationResult(NormalizedJobRequest? request, IDictionary<string, string[]> errors)
{
    public NormalizedJobRequest? Request { get; } = request;
    public IDictionary<string, string[]> Errors { get; } = errors;
    public bool IsValid => Errors.Count == 0;
}
