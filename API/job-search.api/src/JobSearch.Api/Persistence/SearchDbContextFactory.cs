using Microsoft.EntityFrameworkCore.Design;

namespace JobSearch.Api.Persistence;

public sealed class SearchDbContextFactory : IDesignTimeDbContextFactory<SearchDbContext>
{
    public SearchDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var settings = configuration.GetSection(SearchDatabaseOptions.SectionName).Get<SearchDatabaseOptions>() ?? new();
        return new SearchDbContext(SearchPersistence.CreateOptions(settings));
    }
}
