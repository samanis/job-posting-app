using Microsoft.EntityFrameworkCore.Design;

namespace JobPosting.Api.Persistence;

public sealed class PostingDbContextFactory : IDesignTimeDbContextFactory<PostingDbContext>
{
    public PostingDbContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var settings = configuration.GetSection(PostingDatabaseOptions.SectionName).Get<PostingDatabaseOptions>() ?? new();
        return new PostingDbContext(PostingPersistence.CreateOptions(settings));
    }
}
