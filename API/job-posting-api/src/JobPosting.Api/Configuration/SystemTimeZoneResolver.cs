namespace JobPosting.Api.Configuration;

public interface ITimeZoneResolver
{
    TimeZoneInfo Find(string id);
}

// Keep platform lookup replaceable so invalid OS time-zone data can be tested deterministically.
public sealed class SystemTimeZoneResolver : ITimeZoneResolver
{
    public TimeZoneInfo Find(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
}
