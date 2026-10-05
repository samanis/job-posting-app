namespace JobPosting.Api.Configuration;

public sealed class JobPostingOptions
{
    public const string SectionName = "JobPosting";

    public string BusinessTimeZone { get; set; } = "America/Toronto";

    public long MaximumRequestBodyBytes { get; set; } = 65_536;
}
