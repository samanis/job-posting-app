using JobPosting.Api.Contracts;

namespace JobPosting.Api.Validation;

public sealed class NewJobTemporalValidator(TimeProvider clock, TimeZoneInfo businessTimeZone)
{
    // Call only for a NEW key, after attempting lookup/replay using the normalized fingerprint.
    public IDictionary<string, string[]> ValidateNew(NormalizedJobRequest request)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), businessTimeZone).DateTime);
        return request.ClosingDate > today
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]> { ["closingDate"] = ["The closing date must be later than today in the configured business time zone."] };
    }
}
