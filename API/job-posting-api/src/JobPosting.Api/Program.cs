using JobPosting.Api.Messaging;
using JobPosting.Api.Configuration;
using JobPosting.Api.Diagnostics;
using JobPosting.Api.Validation;
using JobPosting.Api.Persistence;
using Microsoft.Extensions.Options;

using JobPosting.Api.Resilience;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
// Framework exception/driver logs can contain arbitrary exception messages or submitted values.
// Application diagnostics record safe failure types and stable identifiers instead.
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Logging.AddFilter("Npgsql", LogLevel.None);
builder.Logging.AddFilter("RabbitMQ.Client", LogLevel.None);
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
builder.Services.AddSingleton<IValidateOptions<ResilienceOptions>, ResilienceOptionsValidator>();
builder.Services.AddOptions<ResilienceOptions>().BindConfiguration(ResilienceOptions.SectionName).ValidateOnStart();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ShutdownDrain>();
builder.Services.AddHostedService<DrainLifetime>();
builder.Services.AddSingleton<IDatabaseProbe, DatabaseProbe>();
builder.Services.AddSingleton<ITimeZoneResolver, SystemTimeZoneResolver>();
builder.Services.AddSingleton<IValidateOptions<JobPostingOptions>, JobPostingOptionsValidator>();
builder.Services.AddOptions<JobPostingOptions>()
    .BindConfiguration(JobPostingOptions.SectionName)
    .ValidateOnStart();
builder.Services.AddSingleton(services => services.GetRequiredService<ITimeZoneResolver>().Find(
    services.GetRequiredService<IOptions<JobPostingOptions>>().Value.BusinessTimeZone));
builder.Services.AddOptions<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>()
    .Configure<IOptions<JobPostingOptions>>((server, posting) =>
        server.Limits.MaxRequestBodySize = posting.Value.MaximumRequestBodyBytes);

builder.Services.AddControllers();
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
builder.Services.AddSingleton<CreateJobRequestReader>();
builder.Services.AddSingleton<JobRequestValidator>();
builder.Services.AddSingleton<NewJobTemporalValidator>();
builder.Services.AddPostingPersistence(builder.Configuration);
builder.Services.AddJobMessaging(builder.Configuration);
builder.Services.AddScoped<JobPosting.Api.Posting.PostingWorkflow>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseExceptionHandler();
app.UseMiddleware<DrainMiddleware>();
app.UseStatusCodePages();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
