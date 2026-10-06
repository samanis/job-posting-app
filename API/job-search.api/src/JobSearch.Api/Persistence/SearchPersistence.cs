using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace JobSearch.Api.Persistence;

public static class SearchPersistence
{
    public static void AddSearchPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);
        services.AddSingleton<IValidateOptions<SearchDatabaseOptions>, SearchDatabaseOptionsValidator>();
        services.AddOptions<SearchDatabaseOptions>().Bind(configuration.GetSection(SearchDatabaseOptions.SectionName)).ValidateOnStart();
        services.AddDbContextFactory<SearchDbContext>((provider, builder) => Configure(builder,
            provider.GetRequiredService<IOptions<SearchDatabaseOptions>>().Value));
        services.AddSingleton<JobSearch.Api.Diagnostics.IDatabaseProbe, JobSearch.Api.Diagnostics.SearchDatabaseProbe>();
        services.AddScoped<ProjectionStore>();
        services.AddScoped<ISearchProjection>(p => p.GetRequiredService<ProjectionStore>());

    }

    public static DbContextOptions<SearchDbContext> CreateOptions(SearchDatabaseOptions settings)
    {
        var builder = new DbContextOptionsBuilder<SearchDbContext>();
        Configure(builder, settings);
        return builder.Options;
    }

    private static void Configure(DbContextOptionsBuilder builder, SearchDatabaseOptions settings)
    {
        // Set before touching the provider: Npgsql caches this process-wide setting on first use.
        AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);
        var validation = new SearchDatabaseOptionsValidator().Validate(null, settings);
        if (validation.Failed) throw new OptionsValidationException(SearchDatabaseOptions.SectionName, typeof(SearchDatabaseOptions), validation.Failures);
        var connection = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
        {
            Timeout = 5,
            CommandTimeout = settings.CommandTimeoutSeconds,
            IncludeErrorDetail = false,
            Options = string.Create(CultureInfo.InvariantCulture, $"-c lock_timeout={settings.LockTimeoutMilliseconds} -c idle_in_transaction_session_timeout={settings.IdleTransactionTimeoutSeconds * 1000} -c statement_timeout={settings.CommandTimeoutSeconds * 1000}")
        };
        builder.UseNpgsql(connection.ConnectionString, postgres => postgres.CommandTimeout(settings.CommandTimeoutSeconds))
            .EnableSensitiveDataLogging(false).EnableDetailedErrors(false);
    }
}
