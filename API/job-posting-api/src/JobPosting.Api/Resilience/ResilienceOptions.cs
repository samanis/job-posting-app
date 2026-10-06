using Microsoft.Extensions.Options;
namespace JobPosting.Api.Resilience;

public sealed class ResilienceOptions
{
    public const string SectionName = "Resilience";
    public double FailureRatio { get; set; } = 0.5;
    public int SamplingSeconds { get; set; } = 60;
    public int MinimumThroughput { get; set; } = 3;
    public int BreakSeconds { get; set; } = 15;
}
public sealed class ResilienceOptionsValidator : IValidateOptions<ResilienceOptions>
{
    public ValidateOptionsResult Validate(string? name, ResilienceOptions options)
    {
        var errors = new List<string>();
        if (options.FailureRatio is <= 0 or > 1 || double.IsNaN(options.FailureRatio)) errors.Add("Resilience:FailureRatio must be greater than zero and at most one.");
        if (options.SamplingSeconds is < 1 or > 60) errors.Add("Resilience:SamplingSeconds must be between 1 and 60.");
        if (options.MinimumThroughput is < 2 or > 1000) errors.Add("Resilience:MinimumThroughput must be between 2 and 1000.");
        if (options.BreakSeconds is < 1 or > 60) errors.Add("Resilience:BreakSeconds must be between 1 and 60.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
