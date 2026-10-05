using System.Security.Cryptography;
using System.Text;
using JobPosting.Api.Contracts;
using JobPosting.Api.Persistence;
using JobPosting.Api.Validation;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace JobPosting.Api.Idempotency;

// Application outcomes only. Even Published requires the future HTTP/publisher workflow;
// Only Created identifies this invocation as the creator eligible for future direct publication.
// Unresolved duplicates and uncertain commits must never trigger another publication.
public enum PostingOutcome { InvalidKey, InvalidRequest, Conflict, InProgress, DependencyUnavailable, Created, PublicationUnresolved, Published }

public sealed class PostingResolution(PostingOutcome outcome, PersistedPosting? posting = null,
    IDictionary<string, string[]>? errors = null)
{
    public PostingOutcome Outcome { get; } = outcome;
    public PersistedPosting? Posting { get; } = posting;
    public IDictionary<string, string[]>? Errors { get; } = errors;
    public int RetryAfterSeconds => 1;
}

public sealed class PostingCoordinator(IPostingWriteStore store, JobRequestValidator validator,
    NewJobTemporalValidator temporal, TimeProvider clock)
{
    public static string Digest(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();

    // Call after strict JSON parsing. No whole-request retries, sleeps or process-local lock.
    public async Task<PostingResolution> ResolveAsync(IReadOnlyList<string?> keys, CreateJobRequest request,
        string correlationId, CancellationToken cancellationToken)
    {
        if (!IdempotencyKeyValidator.TryValidate(keys, out var key)) return new(PostingOutcome.InvalidKey);
        var normalized = validator.Normalize(request);
        if (!normalized.IsValid) return new(PostingOutcome.InvalidRequest, errors: normalized.Errors);
        var payload = normalized.Request!;
        var digest = Digest(key!);
        var fingerprint = JobRequestFingerprint.Compute(payload);
        try
        {
            var existing = await store.ReadAsync(digest, cancellationToken);
            if (existing is not null) return ResolveStored(existing, fingerprint);
            var errors = temporal.ValidateNew(payload);
            if (errors.Count > 0) return new(PostingOutcome.InvalidRequest, errors: errors);
            var pending = PendingPosting.Create(payload, digest, Guid.NewGuid(), Guid.NewGuid(), clock.GetUtcNow(), correlationId);
            PostingResolution result;
            try
            {
                await store.CreateAsync(pending, cancellationToken);
                result = new(PostingOutcome.Created, new(pending.Job));
            }
            catch (PostingCommitUncertainException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // A fresh read can prove a commit, but absence cannot prove rollback.
                result = await ReconcileAsync(digest, fingerprint, cancellationToken);
            }
            catch (Exception exception) when (DatabaseError(exception) is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                // The failed transaction has been disposed by the store; use a clean context.
                result = await ReconcileAsync(digest, fingerprint, cancellationToken);
            }
            catch (Exception exception) when (DatabaseError(exception) is PostgresException { SqlState: PostgresErrorCodes.LockNotAvailable or PostgresErrorCodes.QueryCanceled })
            {
                result = new(PostingOutcome.InProgress);
            }
            catch (Exception exception) when (DatabaseError(exception) is not null)
            {
                result = new(PostingOutcome.DependencyUnavailable);
            }
            return result;
        }
        catch (Exception exception) when (DatabaseError(exception) is not null)
        {
            return new(PostingOutcome.DependencyUnavailable);
        }
    }

    // The non-retrying Npgsql strategy wraps transient failures in InvalidOperationException.
    // Unwrap only known provider/EF shapes; unrelated application failures still reach the handler.
    private static NpgsqlException? DatabaseError(Exception exception) => exception switch
    {
        NpgsqlException database => database,
        DbUpdateException { InnerException: { } inner } => DatabaseError(inner),
        InvalidOperationException { InnerException: { } inner } => DatabaseError(inner),
        _ => null
    };

    private async Task<PostingResolution> ReconcileAsync(string digest, string fingerprint, CancellationToken cancellationToken)
    {
        var existing = await store.ReadAsync(digest, cancellationToken);
        return existing is null ? new(PostingOutcome.DependencyUnavailable) : ResolveStored(existing, fingerprint);
    }

    private static PostingResolution ResolveStored(PersistedPosting posting, string fingerprint)
    {
        // Future canonicalizers must retain versioned readers; never reinterpret an unknown version.
        if (posting.Job.CanonicalizationVersion != JobRequestFingerprint.Version) return new(PostingOutcome.DependencyUnavailable);
        if (posting.Job.RequestFingerprint != fingerprint) return new(PostingOutcome.Conflict);
        return new(posting.Job.PublishedAt.HasValue ? PostingOutcome.Published : PostingOutcome.PublicationUnresolved, posting);
    }
}
