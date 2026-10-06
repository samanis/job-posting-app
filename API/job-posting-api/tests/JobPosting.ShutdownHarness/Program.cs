using JobPosting.Api.Contracts;
using JobPosting.Api.Configuration;
using JobPosting.Api.Diagnostics;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;
using JobPosting.Api.Posting;
using JobPosting.Api.Resilience;
using JobPosting.Api.Validation;
using Microsoft.Extensions.Options;

// Test-only Linux/Kestrel host. No test hooks are compiled into the deployed API.
var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders(); builder.Logging.AddJsonConsole();
builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", LogLevel.None);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
builder.Services.AddSingleton<TimeProvider>(new FixedClock());
builder.Services.AddSingleton(TimeZoneInfo.Utc);
builder.Services.Configure<JobPostingOptions>(_ => { });
builder.Services.AddSingleton<CreateJobRequestReader>(); builder.Services.AddSingleton<JobRequestValidator>(); builder.Services.AddSingleton<NewJobTemporalValidator>();
builder.Services.AddPostingPersistence(builder.Configuration); builder.Services.AddJobMessaging(builder.Configuration);
builder.Services.AddSingleton<ShutdownDrain>(); builder.Services.AddHostedService<DrainLifetime>();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Services.AddOptions<ResilienceOptions>();
builder.Services.AddScoped<IPostingWriteStore>(s => new DelayedStore(s.GetRequiredService<PostingWriteStore>(), s.GetRequiredService<ILogger<DelayedStore>>()));
builder.Services.AddSingleton<IJobEventPublisher>(s => new DelayedPublisher(s.GetRequiredService<PublicationCircuit>(), s.GetRequiredService<ILogger<DelayedPublisher>>()));
builder.Services.AddScoped<PostingWorkflow>(); builder.Services.AddSingleton<IDatabaseProbe, DatabaseProbe>();
builder.Services.AddControllers().AddApplicationPart(typeof(JobsController).Assembly);
builder.Services.AddProblemDetails(); builder.Services.AddExceptionHandler<UnexpectedExceptionHandler>();
var app = builder.Build(); app.UseRouting(); app.UseMiddleware<RequestDiagnosticsMiddleware>(); app.UseExceptionHandler(); app.UseMiddleware<DrainMiddleware>(); app.MapControllers(); app.Run();

sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero); }
sealed class DelayedPublisher(IJobEventPublisher publisher, ILogger<DelayedPublisher> logger) : IJobEventPublisher
{
    public async Task PublishAsync(JobPostingCreated message, CancellationToken token)
    {
        var mode = Environment.GetEnvironmentVariable("SIGNAL_TEST_MODE")!;
        if (mode.Contains("cleanup", StringComparison.Ordinal)) throw new JobPublicationException(PublicationFailure.NotAccepted, new IOException("test failure"));
        logger.LogInformation("PublicationWindow");
        await Task.Delay(TimeSpan.FromSeconds(mode.StartsWith("forced", StringComparison.Ordinal) ? 30 : 3), token);
        await publisher.PublishAsync(message, token);
    }
}
sealed class DelayedStore(IPostingWriteStore store, ILogger<DelayedStore> logger) : IPostingWriteStore
{
    public Task CreateAsync(PendingPosting row, CancellationToken token) => store.CreateAsync(row, token);
    public Task<PersistedPosting?> ReadAsync(string digest, CancellationToken token) => store.ReadAsync(digest, token);
    public Task<bool> MarkPublishedAsync(Guid job, Guid message, DateTimeOffset at, CancellationToken token) => store.MarkPublishedAsync(job, message, at, token);
    public async Task<bool> DeleteUnpublishedAsync(Guid job, Guid message, CancellationToken token)
    {
        logger.LogInformation("CleanupWindow");
        await Task.Delay(TimeSpan.FromSeconds(Environment.GetEnvironmentVariable("SIGNAL_TEST_MODE")!.StartsWith("forced", StringComparison.Ordinal) ? 30 : 1.5), token);
        return await store.DeleteUnpublishedAsync(job, message, token);
    }
}
