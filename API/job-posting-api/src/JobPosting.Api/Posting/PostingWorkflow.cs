using JobPosting.Api.Contracts;
using JobPosting.Api.Idempotency;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;

namespace JobPosting.Api.Posting;

public sealed class PostingWorkflowException(string code, Exception innerException)
    : Exception("The posting could not be completed. The outcome may require investigation.", innerException)
{
    public string Code { get; } = code;
}

public sealed class PostingWorkflow(PostingCoordinator coordinator, IPostingWriteStore store,
    IJobEventPublisher publisher, TimeProvider clock, ILogger<PostingWorkflow> logger, JobPosting.Api.Resilience.ShutdownDrain? drain = null)
{
    public async Task<PostingResolution> SubmitAsync(IReadOnlyList<string?> keys, CreateJobRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        var result = await coordinator.ResolveAsync(keys, request, correlationId, cancellationToken);
        if (result.Outcome != PostingOutcome.Created) return result;
        var job = result.Posting!.Job;
        try
        {
            await publisher.PublishAsync(JobPostingCreated.FromSaved(job, correlationId), cancellationToken);
        }
        catch (Exception publicationFailure)
        {
            // Cancellation of the request must not skip compensation of an already saved job.
            using var cleanup = new CancellationTokenSource(drain?.CleanupBudget ?? TimeSpan.FromSeconds(3));
            try
            {
                if (!await store.DeleteUnpublishedAsync(job.Id, job.EventId, cleanup.Token).WaitAsync(cleanup.Token))
                    throw new InvalidOperationException("The exact unpublished job could not be deleted.");
            }
            catch (Exception cleanupFailure)
            {
                JobPosting.Api.Diagnostics.PostingMetrics.Cleanup("failed");
                var failures = new AggregateException(publicationFailure, cleanupFailure);
                logger.LogCritical(new EventId(2001, "PostingCleanupFailed"),
                    "Posting cleanup failed; job {JobId}, event {EventId}, trace {TraceId}; publication {PublicationFailure}, cleanup {CleanupFailure}", job.Id, job.EventId, correlationId, publicationFailure.GetType().Name, cleanupFailure.GetType().Name);
                throw new PostingWorkflowException(JobApiProblems.PublicationUnresolved, failures);
            }
            JobPosting.Api.Diagnostics.PostingMetrics.Cleanup("deleted");
            throw new PostingWorkflowException(JobApiProblems.PublicationFailed, publicationFailure);
        }
        try
        {
            if (!await store.MarkPublishedAsync(job.Id, job.EventId, clock.GetUtcNow(), cancellationToken))
                throw new InvalidOperationException("Publication state could not be recorded for the exact job.");
        }
        catch (Exception exception)
        {
            // Known broker acceptance cannot be undone by deleting a database row.
            logger.LogCritical(new EventId(2002, "PostingPublicationStateFailed"),
                "Confirmed publication state was not recorded; job {JobId}, event {EventId}, trace {TraceId}; failure {Failure}", job.Id, job.EventId, correlationId, exception.GetType().Name);
            throw new PostingWorkflowException(JobApiProblems.PublicationUnresolved, exception);
        }
        return new(PostingOutcome.Published, result.Posting);
    }
}
