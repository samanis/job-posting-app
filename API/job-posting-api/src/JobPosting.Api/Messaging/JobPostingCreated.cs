using JobPosting.Api.Contracts;
using JobPosting.Api.Persistence;

namespace JobPosting.Api.Messaging;

public sealed class JobPostingCreated(Guid eventId, DateTimeOffset occurredAt, string correlationId, SavedJobRecord job)
{
    public Guid EventId { get; } = eventId;
    public string EventType => "JobPostingCreated";
    public int SchemaVersion => 1;
    public DateTimeOffset OccurredAt { get; } = occurredAt.ToUniversalTime();
    public string CorrelationId { get; } = correlationId;
    public SavedJobRecord Job { get; } = job;

    public static JobPostingCreated FromSaved(JobPostingEntity row, string correlationId) => new(row.EventId, row.CreatedAt, correlationId,
        new SavedJobRecord
        {
            Id = row.Id.ToString("D"),
            CreatedAt = row.CreatedAt,
            Title = row.Title,
            Department = row.Department,
            Location = row.Location,
            Description = row.Description,
            SalaryMin = row.SalaryMin,
            SalaryMax = row.SalaryMax,
            ClosingDate = row.ClosingDate
        });
}

public interface IJobEventPublisher
{
    Task PublishAsync(JobPostingCreated envelope, CancellationToken cancellationToken);
}

public enum PublicationFailure { NotAccepted, AcceptanceUnknown }
public sealed class JobPublicationException(PublicationFailure failure, Exception innerException)
    : Exception("The job message could not be confirmed by the broker.", innerException)
{
    public PublicationFailure Failure { get; } = failure;
}
