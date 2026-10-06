using JobSearch.Api.Contracts;
using Microsoft.EntityFrameworkCore;
using Npgsql;
namespace JobSearch.Api.Persistence;
public enum ProjectionOutcome { Inserted, Duplicate, Conflict }
public sealed class ProjectionCommitUncertainException(Exception failure) : Exception("Projection commit outcome is uncertain; no acknowledgment is authorized.",failure);
public interface ISearchProjection
{
    Task<ProjectionOutcome> ProjectAsync(JobCreatedEvent message,CancellationToken token);
}
public sealed class ProjectionStore(IDbContextFactory<SearchDbContext> contexts) : ISearchProjection
{
    public async Task<ProjectionOutcome> ProjectAsync(JobCreatedEvent message,CancellationToken token)
    {
        var row=SearchJob.FromEvent(message);
        try { return await InsertAsync(row,token); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: "23505", ConstraintName: "pk_jobs" or "ux_jobs_event_id" })
        {
            var existing=await ReadIdentitiesAsync(row,token);
            if(existing.Count==0) throw;
            return Classify(existing,row);
        }
        catch (ProjectionCommitUncertainException)
        {
            // Fresh durable read; failed/missing/conflicting read cannot authorize ACK.
            var existing=await ReadIdentitiesAsync(row,token);
            if (Classify(existing,row)==ProjectionOutcome.Duplicate) return ProjectionOutcome.Duplicate;
            throw;
        }
    }
    private async Task<List<SearchJob>> ReadIdentitiesAsync(SearchJob row,CancellationToken token)
    {
        await using var fresh=await contexts.CreateDbContextAsync(token);
        return await fresh.FindIdentitiesAsync(row.Id,row.EventId,token);
    }
    private async Task<ProjectionOutcome> InsertAsync(SearchJob row,CancellationToken token)
    {
        await using var context=await contexts.CreateDbContextAsync(token);
        await using var transaction=await context.Database.BeginTransactionAsync(token);
        await context.LockIngestionAsync(token);
        var existing=await context.FindIdentitiesAsync(row.Id,row.EventId,token);
        if(existing.Count>0) return Classify(existing,row);
        row.IngestionSequence=await context.AllocateSequenceAsync(token);
        context.AddRange(row);
        await context.SaveChangesAsync(token);
        try { await transaction.CommitAsync(token); }
        catch(Exception failure) { throw new ProjectionCommitUncertainException(failure); }
        return ProjectionOutcome.Inserted;
    }
    public static ProjectionOutcome Classify(IReadOnlyList<SearchJob> existing,SearchJob row) =>
        existing.Count==1 && existing[0].Id==row.Id && existing[0].EventId==row.EventId && existing[0].PayloadHash==row.PayloadHash && existing[0].HashVersion==row.HashVersion
            ? ProjectionOutcome.Duplicate : ProjectionOutcome.Conflict;
    public async Task<long> ReadWatermarkAsync(CancellationToken token)
    {
        await using var context=await contexts.CreateDbContextAsync(token);
        return await context.ReadWatermarkAsync(token);
    }
}
