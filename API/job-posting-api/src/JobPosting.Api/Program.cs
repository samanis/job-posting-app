using JobPosting.Api.Configuration;
using JobPosting.Api.Diagnostics;
using JobPosting.Api.Validation;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
});

builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);
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

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddOpenApi();
}

var app = builder.Build();

app.UseRouting();
app.UseMiddleware<RequestDiagnosticsMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.MapControllers();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.Run();

public partial class Program;
