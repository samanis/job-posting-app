using System.Text.Json;
using JobPosting.Api.Contracts;
using JobPosting.Api.Validation;

namespace JobPosting.Api.Persistence;

public sealed class PendingPosting(JobPostingEntity job)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public JobPostingEntity Job { get; } = job;

    public static PendingPosting Create(NormalizedJobRequest request, string keyDigest, Guid jobId, Guid eventId,
        DateTimeOffset createdAt, string correlationId)
    {
        // PostgreSQL timestamps store microseconds; snapshots must use the same authoritative precision.
        var utc = createdAt.ToUniversalTime();
        var databaseTime = new DateTimeOffset(utc.Ticks - utc.Ticks % TimeSpan.TicksPerMicrosecond, TimeSpan.Zero);
        var saved = SavedJobRecord.Create(request, jobId, databaseTime);
        var job = new JobPostingEntity
        {
            Id = jobId, CreatedAt = saved.CreatedAt, Title = saved.Title, Department = saved.Department,
            Location = saved.Location, Description = saved.Description, SalaryMin = saved.SalaryMin,
            SalaryMax = saved.SalaryMax, ClosingDate = saved.ClosingDate,
            IdempotencyKeyDigest = keyDigest, RequestFingerprint = JobRequestFingerprint.Compute(request),
            ResponseJson = JsonSerializer.Serialize(JobAcceptedResponse.FromSaved(saved), JsonOptions)
        };
        job.CanonicalizationVersion = JobRequestFingerprint.Version;
        job.EventId = eventId;
        return new(job);
    }
}
