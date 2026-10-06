using JobSearch.Api.Configuration;
namespace JobSearch.Api.Tests;
[Trait("Category", "Unit")]
public sealed class ConfigurationTests
{
    [Theory]
    [InlineData(1, true)]
    [InlineData(65536, true)]
    [InlineData(1048576, true)]
    [InlineData(0, false)]
    [InlineData(1048577, false)]
    public void EventLimitIsBounded(int limit, bool valid)
    {
        var options = new SearchOptions();
        Assert.Equal(65536, options.MaximumEventBodyBytes);
        Assert.Equal("Search", SearchOptions.SectionName);
        options.MaximumEventBodyBytes = limit;
        var result = new SearchOptionsValidator().Validate(null, options);
        Assert.Equal(valid, result.Succeeded);
        if (!valid) Assert.Contains("Search:MaximumEventBodyBytes", result.FailureMessage);
    }
}
