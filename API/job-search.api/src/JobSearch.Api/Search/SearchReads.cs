using JobSearch.Api.Contracts;
using Microsoft.Extensions.Options;
namespace JobSearch.Api.Search;

public static class SearchReads
{
    public static void AddSearchReads(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddSingleton<IValidateOptions<CursorOptions>, CursorOptionsValidator>();
        services.AddOptions<CursorOptions>().Bind(configuration.GetSection("Cursor")).PostConfigure(o => { if (environment.IsDevelopment() && o.Keys.Count == 0) o.Keys["current"] = CursorOptions.DevelopmentKey; }).ValidateOnStart();
        services.AddSingleton<QueryReader>(); services.AddSingleton<CursorCodec>(); services.AddScoped<IJobReadStore, JobReadStore>();
        services.AddControllers().AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new UtcTimestampConverter()));
    }
}
