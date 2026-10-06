using Microsoft.Extensions.Options;
namespace JobSearch.Api.Configuration;

public sealed class SearchOptions
{
    public const string SectionName = "Search";
    public int MaximumEventBodyBytes { get; set; } = 65536;
}

public sealed class SearchOptionsValidator : IValidateOptions<SearchOptions>
{
    public ValidateOptionsResult Validate(string? name, SearchOptions options) =>
        options.MaximumEventBodyBytes is >= 1 and <= 1048576
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail("Search:MaximumEventBodyBytes must be between 1 and 1048576.");
}
