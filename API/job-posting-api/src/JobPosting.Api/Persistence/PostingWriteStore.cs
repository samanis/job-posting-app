using Microsoft.EntityFrameworkCore;

namespace JobPosting.Api.Persistence;

public interface IPostingWriteStore
{
    Task<bool> MarkPublishedAsync(Guid jobId, Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken);
    Task<bool> DeleteUnpublishedAsync(Guid jobId, Guid eventId, CancellationToken cancellationToken);
    Task CreateAsync(PendingPosting posting, CancellationToken cancellationToken);
    Task<PersistedPosting?> ReadAsync(string keyDigest, CancellationToken cancellationToken);
}

public sealed class PostingWriteStore(IDbContextFactory<PostingDbContext> contexts) : IPostingWriteStore
{
    public async Task CreateAsync(PendingPosting posting, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        context.AddRange(posting.Job);
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            // No automatic retry/re-create and no assertion that disposal proves rollback.
            throw new PostingCommitUncertainException(exception);
        }
    }

    public async Task<PersistedPosting?> ReadAsync(string keyDigest, CancellationToken cancellationToken)
    {
        // Always a fresh context; reconciliation must not read cached attempted inserts after an uncertain commit.
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var job = await context.FindByDigestAsync(keyDigest, cancellationToken);
        return job is null ? null : new(job);
    }

    public async Task<bool> MarkPublishedAsync(Guid jobId, Guid eventId, DateTimeOffset publishedAt, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var job = await context.Jobs.FindAsync([jobId], cancellationToken);
        if (job is null || job.EventId != eventId) return false;
        if (job.PublishedAt.HasValue) return true;
        var utc = publishedAt.ToUniversalTime();
        job.PublishedAt = new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DeleteUnpublishedAsync(Guid jobId, Guid eventId, CancellationToken cancellationToken)
    {
        await using var context = await contexts.CreateDbContextAsync(cancellationToken);
        var job = await context.Jobs.FindAsync([jobId], cancellationToken);
        if (job is null || job.EventId != eventId || job.PublishedAt.HasValue) return false;
        context.Remove(job);
        await context.SaveChangesAsync(cancellationToken);
        return true;
    }

}

public sealed class PersistedPosting(JobPostingEntity job)
{
    public JobPostingEntity Job { get; } = job;
}

public sealed class PostingCommitUncertainException(Exception innerException)
    : Exception("The posting transaction commit outcome is uncertain. Reconcile using the same key.", innerException);
