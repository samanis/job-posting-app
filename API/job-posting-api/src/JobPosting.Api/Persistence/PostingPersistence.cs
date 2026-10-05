using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace JobPosting.Api.Persistence;

public static class PostingPersistence
{
    public static void AddPostingPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);
        services.AddSingleton<IValidateOptions<PostingDatabaseOptions>, PostingDatabaseOptionsValidator>();
        services.AddOptions<PostingDatabaseOptions>().Bind(configuration.GetSection(PostingDatabaseOptions.SectionName)).ValidateOnStart();
        services.AddDbContextFactory<PostingDbContext>((provider, builder) => Configure(builder,
            provider.GetRequiredService<IOptions<PostingDatabaseOptions>>().Value));
        services.AddScoped<PostingWriteStore>();
        services.AddScoped<IPostingWriteStore>(provider => provider.GetRequiredService<PostingWriteStore>());
        services.AddScoped<JobPosting.Api.Idempotency.PostingCoordinator>();
    }

    public static DbContextOptions<PostingDbContext> CreateOptions(PostingDatabaseOptions settings)
    {
        var builder = new DbContextOptionsBuilder<PostingDbContext>();
        Configure(builder, settings);
        return builder.Options;
    }

    private static void Configure(DbContextOptionsBuilder builder, PostingDatabaseOptions settings)
    {
        // Set before touching the provider: Npgsql caches this process-wide setting on first use.
        AppContext.SetSwitch("Npgsql.DisableDateTimeInfinityConversions", true);
        var validation = new PostingDatabaseOptionsValidator().Validate(null, settings);
        if (validation.Failed) throw new OptionsValidationException(PostingDatabaseOptions.SectionName, typeof(PostingDatabaseOptions), validation.Failures);
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
