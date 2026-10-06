using JobSearch.Api.Messaging;
using JobSearch.Api.Persistence;
using JobSearch.Api.Configuration;
using JobSearch.Api.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
// Framework diagnostics may log arbitrary paths or exception details.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.None);
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IValidateOptions<SearchOptions>, SearchOptionsValidator>();
builder.Services.AddOptions<SearchOptions>().BindConfiguration(SearchOptions.SectionName).ValidateOnStart();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Npgsql", LogLevel.None);
builder.Services.AddSearchPersistence(builder.Configuration);
builder.Logging.AddFilter("RabbitMQ.Client", LogLevel.None);
builder.Services.AddSearchMessaging(builder.Configuration);
JobSearch.Api.Search.SearchReads.AddSearchReads(builder.Services, builder.Configuration, builder.Environment);
JobSearch.Api.Search.SearchCaching.AddSearchCaching(builder.Services);
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}
var app = builder.Build();
app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseExceptionHandler();
app.UseRouting();
app.UseStatusCodePages();
// After routing so the [OutputCache] endpoint policies are visible.
app.UseOutputCache();
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
app.Run();
public partial class Program;
