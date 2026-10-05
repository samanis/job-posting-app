using JobPosting.Api.Configuration;

namespace JobPosting.Api.Tests;

[Trait("Category", "Unit")]
public sealed class ConfigurationTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(1048576)]
    public void SupportedTimezoneAndBodyLimitBoundariesAreAccepted(long maximumBodyBytes)
    {
        var result = new JobPostingOptionsValidator(new SystemTimeZoneResolver()).Validate(null, new JobPostingOptions
        {
            BusinessTimeZone = "UTC",
            MaximumRequestBodyBytes = maximumBodyBytes
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void InvalidConfigurationReportsBothProblemsWithoutEchoingValues()
    {
        var result = new JobPostingOptionsValidator(new SystemTimeZoneResolver()).Validate(null, new JobPostingOptions
        {
            BusinessTimeZone = "unknown-private-value",
            MaximumRequestBodyBytes = -1
        });

        Assert.True(result.Failed);
        Assert.Equal(2, result.Failures.Count());
        Assert.Contains("JobPosting:BusinessTimeZone", result.FailureMessage);
        Assert.Contains("JobPosting:MaximumRequestBodyBytes", result.FailureMessage);
        Assert.DoesNotContain("unknown-private-value", result.FailureMessage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingTimeZoneIsRejected(string? timeZone)
    {
        var result = new JobPostingOptionsValidator(new SystemTimeZoneResolver()).Validate(null,
            new JobPostingOptions { BusinessTimeZone = timeZone! });
        Assert.True(result.Failed);
        Assert.Single(result.Failures);
    }

    [Fact]
    public void InvalidPlatformTimeZoneDataIsRejectedWithoutLeakingDetails()
    {
        var result = new JobPostingOptionsValidator(new CorruptTimeZones()).Validate(null, new JobPostingOptions());
        Assert.True(result.Failed);
        Assert.Single(result.Failures);
        Assert.DoesNotContain("private-platform-detail", result.FailureMessage);
    }

    [Fact]
    public void OversizedRequestLimitIsRejected()
    {
        var result = new JobPostingOptionsValidator(new SystemTimeZoneResolver()).Validate(null,
            new JobPostingOptions { MaximumRequestBodyBytes = 1_048_577 });
        Assert.True(result.Failed);
        Assert.Contains("MaximumRequestBodyBytes", result.FailureMessage);
    }

    private sealed class CorruptTimeZones : ITimeZoneResolver
    {
        public TimeZoneInfo Find(string id) => throw new InvalidTimeZoneException("private-platform-detail");
    }
}
