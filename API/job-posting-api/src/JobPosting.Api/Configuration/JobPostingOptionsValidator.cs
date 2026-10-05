using Microsoft.Extensions.Options;

namespace JobPosting.Api.Configuration;

public sealed class JobPostingOptionsValidator(ITimeZoneResolver timeZones) : IValidateOptions<JobPostingOptions>
{
    public ValidateOptionsResult Validate(string? name, JobPostingOptions options)
    {
        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BusinessTimeZone))
        {
            failures.Add("JobPosting:BusinessTimeZone must identify a supported time zone.");
        }
        else
        {
            try
            {
                timeZones.Find(options.BusinessTimeZone);
            }
            catch (TimeZoneNotFoundException)
            {
                failures.Add("JobPosting:BusinessTimeZone must identify a supported time zone.");
            }
            catch (InvalidTimeZoneException)
            {
                failures.Add("JobPosting:BusinessTimeZone must identify a supported time zone.");
            }
        }

        if (options.MaximumRequestBodyBytes is < 1 or > 1_048_576)
        {
            failures.Add("JobPosting:MaximumRequestBodyBytes must be between 1 and 1048576.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
