using System.Text;
using System.Text.Json;
using JobPosting.Api.Configuration;
using JobPosting.Api.Contracts;
using JobPosting.Api.Diagnostics;
using JobPosting.Api.Idempotency;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;
using JobPosting.Api.Posting;
using JobPosting.Api.Tests.Support;
using JobPosting.Api.Validation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;

namespace JobPosting.Api.Tests;

[Trait("Category", "Unit")]
public sealed class PostingWorkflowTests
{
    public static CreateJobRequest Request() => new() { Title = " Engineer ", Department = "Engineering", Location = "Toronto", Description = "Plain text", SalaryMin = 10m, SalaryMax = 100m, ClosingDate = new(2028, 2, 29) };
    [Fact]
    public async Task SuccessPublishesOnlyAfterSaveAndReplayNeverSendsAgain()
    {
        using var harness = new Harness();
        var result = await harness.Workflow.SubmitAsync(["key"], Request(), "trace", default);
        Assert.Equal(PostingOutcome.Published, result.Outcome);
        Assert.Equal(new[] { "read", "create", "publish", "mark" }, harness.Calls);
        Assert.Equal(harness.Job!.Id.ToString("D"), harness.Envelope!.Job.Id);
        Assert.Equal(harness.Job.EventId, harness.Envelope.EventId);
        Assert.Equal("trace", harness.Envelope.CorrelationId);
        var replay = await harness.Workflow.SubmitAsync(["key"], Request(), "retry", default);
        Assert.Equal(result.Posting!.Job.ResponseJson, replay.Posting!.Job.ResponseJson);
        Assert.Equal(1, harness.Calls.Count(value => value == "publish"));
        Assert.DoesNotContain("delete", harness.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublicationFailureOrRequestCancellationDeletesWithIndependentToken(bool cancelled)
    {
        using var harness = new Harness(); using var cancellation = new CancellationTokenSource();
        harness.Publish = (_, _) => { if (cancelled) cancellation.Cancel(); return Task.FromException(cancelled ? new OperationCanceledException(cancellation.Token) : new JobPublicationException(PublicationFailure.AcceptanceUnknown, new IOException("lost"))); };
        var error = await Assert.ThrowsAsync<PostingWorkflowException>(() => harness.Workflow.SubmitAsync(["key"], Request(), "trace", cancellation.Token));
        Assert.Equal(JobApiProblems.PublicationFailed, error.Code);
        Assert.Null(harness.Job); Assert.DoesNotContain("mark", harness.Calls);
        Assert.True(harness.CleanupToken.CanBeCanceled); Assert.False(harness.CleanupToken.IsCancellationRequested);
        Assert.NotEqual(cancellation.Token, harness.CleanupToken);
        Assert.DoesNotContain(harness.Logs.Entries, entry => entry.Level == LogLevel.Critical);
    }
    [Theory]
    [InlineData("error")]
    [InlineData("false")]
    [InlineData("cancel")]
    [InlineData("timeout")]
    public async Task CleanupFailureRetainsRecordAndLogsBothFailures(string mode)
    {
        using var harness = new Harness(); var publication = new IOException("private publication detail");
        harness.Publish = (_, _) => Task.FromException(publication);
        harness.Delete = token => mode switch { "false" => Task.FromResult(false), "cancel" => Task.FromException<bool>(new OperationCanceledException()), "timeout" => Task.Delay(Timeout.Infinite, token).ContinueWith<bool>(_ => throw new OperationCanceledException(token), TaskScheduler.Default), _ => Task.FromException<bool>(new IOException("private cleanup detail")) };
        var error = await Assert.ThrowsAsync<PostingWorkflowException>(() => harness.Workflow.SubmitAsync(["key"], Request(), "trace", default));
        Assert.Equal(JobApiProblems.PublicationUnresolved, error.Code);
        var failures = Assert.IsType<AggregateException>(error.InnerException); Assert.Same(publication, failures.InnerExceptions[0]); Assert.Equal(2, failures.InnerExceptions.Count);
        var log = Assert.Single(harness.Logs.Entries, entry => entry.Level == LogLevel.Critical);
        Assert.Equal("PostingCleanupFailed", log.EventId.Name); Assert.Equal(harness.Job!.Id, log.Properties["JobId"]); Assert.Equal(harness.Job.EventId, log.Properties["EventId"]);
        Assert.Null(log.Exception); Assert.Equal("IOException", log.Properties["PublicationFailure"]); Assert.Equal(failures.InnerExceptions[1].GetType().Name, log.Properties["CleanupFailure"]); Assert.DoesNotContain("mark", harness.Calls);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConfirmedPublicationWithStatusFailureKeepsJobAndNeverCompensates(bool falseResult)
    {
        using var harness = new Harness(); harness.Mark = () => falseResult ? Task.FromResult(false) : Task.FromException<bool>(new IOException("private status failure"));
        var error = await Assert.ThrowsAsync<PostingWorkflowException>(() => harness.Workflow.SubmitAsync(["key"], Request(), "trace", default));
        Assert.Equal(JobApiProblems.PublicationUnresolved, error.Code); Assert.NotNull(harness.Job);
        Assert.DoesNotContain("delete", harness.Calls); Assert.Equal("PostingPublicationStateFailed", Assert.Single(harness.Logs.Entries, entry => entry.Level == LogLevel.Critical).EventId.Name);
    }
    [Theory]
    [InlineData("success", 202)]
    [InlineData("key", 400)]
    [InlineData("semantic", 422)]
    [InlineData("conflict", 409)]
    [InlineData("unresolved", 503)]
    [InlineData("database", 503)]
    [InlineData("contention", 409)]
    [InlineData("json", 400)]
    [InlineData("precision", 422)]
    [InlineData("utf8", 400)]
    [InlineData("size", 413)]
    public async Task ControllerMapsStrictRequestAndAllOutcomesSafely(string mode, int status)
    {
        using var harness = new Harness(); var request = Request(); var key = "key";
        if (mode is "conflict" or "unresolved") { var pending = PendingPosting.Create(new JobRequestValidator().Normalize(request).Request!, PostingCoordinator.Digest(key), Guid.NewGuid(), Guid.NewGuid(), harness.Clock.GetUtcNow(), "trace"); harness.Job = pending.Job; if (mode == "conflict") harness.Job.RequestFingerprint = new string('b', 64); }
        if (mode == "semantic") request = new();
        if (mode == "key") key = "invalid key";
        if (mode == "database") harness.ReadFailure = new NpgsqlException("private database");
        if (mode == "contention") harness.CreateFailure = new Microsoft.EntityFrameworkCore.DbUpdateException("private", new PostgresException("lock", "ERROR", "ERROR", PostgresErrorCodes.LockNotAvailable));
        var json = JsonSerializer.Serialize(request, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (mode == "json") json = "{";
        if (mode == "precision") json = json.Replace("10,", "10.001,", StringComparison.Ordinal);
        var bytes = mode == "utf8" ? new byte[] { 0xff } : Encoding.UTF8.GetBytes(json);
        var context = new DefaultHttpContext(); context.TraceIdentifier = "trace"; context.Request.Headers["Idempotency-Key"] = key; context.Request.Body = new MemoryStream(bytes);
        var controller = new JobsController(new(), harness.Workflow, Options.Create(new JobPostingOptions { MaximumRequestBodyBytes = mode == "size" ? 5 : 65536 })) { ControllerContext = new() { HttpContext = context } };
        var response = await controller.Create(default);
        if (status == 202) { var content = Assert.IsType<ContentResult>(response); Assert.Equal(status, content.StatusCode); Assert.Equal(harness.Job!.ResponseJson, content.Content); }
        else { var problem = Assert.IsType<ObjectResult>(response); Assert.Equal(status, problem.StatusCode); Assert.Contains("application/problem+json", problem.ContentTypes); Assert.Equal("trace", Assert.IsAssignableFrom<ProblemDetails>(problem.Value).Extensions["traceId"]); }
        Assert.Equal(mode is "unresolved" or "database" or "contention" ? "1" : "", context.Response.Headers.RetryAfter.ToString());
    }
    [Fact]
    public async Task CentralHandlerMapsWorkflowExceptionWithoutDiagnosticLeak()
    {
        var writer = new Writer(); var handler = new UnexpectedExceptionHandler(writer, NullLogger<UnexpectedExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        Assert.True(await handler.TryHandleAsync(context, new PostingWorkflowException(JobApiProblems.PublicationUnresolved, new IOException("private password")), default));
        Assert.Equal(503, context.Response.StatusCode); Assert.Equal("1", context.Response.Headers.RetryAfter.ToString());
        Assert.DoesNotContain("private", JsonSerializer.Serialize(writer.Problem));
        Assert.True(await handler.TryHandleAsync(context, new BadHttpRequestException("private limit detail", 413), default));
        Assert.Equal(413, context.Response.StatusCode);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(writer.Problem));
    }
    private sealed class Writer : IProblemDetailsService
    {
        public ProblemDetails? Problem;
        public ValueTask<bool> TryWriteAsync(ProblemDetailsContext context) { Problem = context.ProblemDetails; return ValueTask.FromResult(true); }
        public ValueTask WriteAsync(ProblemDetailsContext context) => ValueTask.CompletedTask;
    }
    public sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero); }
    public sealed class Harness : IPostingWriteStore, IJobEventPublisher, IDisposable
    {
        public List<string> Calls = []; public JobPostingEntity? Job; public JobPostingCreated? Envelope;
        public Exception? ReadFailure, CreateFailure;
        public Func<JobPostingCreated, CancellationToken, Task> Publish = (_, _) => Task.CompletedTask;
        public Func<CancellationToken, Task<bool>>? Delete; public Func<Task<bool>>? Mark;
        public CancellationToken CleanupToken; public Clock Clock = new(); public CapturedLoggerProvider Logs = new();
        private readonly ILoggerFactory logFactory;
        public PostingWorkflow Workflow { get; }
        public Harness() { logFactory = LoggerFactory.Create(builder => builder.AddProvider(Logs)); Workflow = new(new(this, new(), new(Clock, TimeZoneInfo.Utc), Clock), this, this, Clock, logFactory.CreateLogger<PostingWorkflow>()); }
        public Task<PersistedPosting?> ReadAsync(string digest, CancellationToken token) { Calls.Add("read"); if (ReadFailure is not null) return Task.FromException<PersistedPosting?>(ReadFailure); return Task.FromResult(Job is null ? null : new PersistedPosting(Job)); }
        public Task CreateAsync(PendingPosting posting, CancellationToken token) { Calls.Add("create"); if (CreateFailure is not null) return Task.FromException(CreateFailure); Job = posting.Job; return Task.CompletedTask; }
        public Task PublishAsync(JobPostingCreated envelope, CancellationToken token) { Calls.Add("publish"); Envelope = envelope; return Publish(envelope, token); }
        public Task<bool> DeleteUnpublishedAsync(Guid jobId, Guid eventId, CancellationToken token) { Calls.Add("delete"); CleanupToken = token; if (Delete is not null) return Delete(token); Assert.Equal(Job!.Id, jobId); Assert.Equal(Job.EventId, eventId); Job = null; return Task.FromResult(true); }
        public Task<bool> MarkPublishedAsync(Guid jobId, Guid eventId, DateTimeOffset at, CancellationToken token) { Calls.Add("mark"); if (Mark is not null) return Mark(); Job!.PublishedAt = at; return Task.FromResult(true); }
        public void Dispose() => logFactory.Dispose();
    }
}
