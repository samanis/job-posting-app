using JobSearch.Api.Contracts;
namespace JobSearch.Api.Persistence;

public sealed class SearchJob
{
    public Guid Id { get; set; }
    public Guid EventId { get; set; }
    public string PayloadHash { get; set; } = "";
    public int HashVersion { get; set; }
    public long IngestionSequence { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    // PostgreSQL stores microseconds; retain original 100ns source ticks independently.
    public long SourceOccurredAtTicks { get; set; }
    public long SourceCreatedAtTicks { get; set; }
    public string Title { get; set; } = "";
    public string Department { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public decimal SalaryMin { get; set; }
    public decimal SalaryMax { get; set; }
    public DateOnly ClosingDate { get; set; }
    public static SearchJob FromEvent(JobCreatedEvent e)
    {
        var j = e.Job;
        return new()
        {
            Id = j.Id,
            EventId = e.EventId,
            PayloadHash = EventFingerprint.Compute(e),
            HashVersion = EventFingerprint.Version,
            OccurredAt = e.OccurredAt.ToUniversalTime(),
            CreatedAt = j.CreatedAt.ToUniversalTime(),
            SourceOccurredAtTicks = e.OccurredAt.UtcTicks,
            SourceCreatedAtTicks = j.CreatedAt.UtcTicks,
            Title = j.Title,
            Department = j.Department,
            Location = j.Location,
            Description = j.Description,
            SalaryMin = j.SalaryMin,
            SalaryMax = j.SalaryMax,
            ClosingDate = j.ClosingDate
        };
    }
    public ProjectedJob ToJob() => new(Id, new DateTimeOffset(SourceCreatedAtTicks, TimeSpan.Zero), Title, Department, Location, Description, SalaryMin, SalaryMax, ClosingDate);
}
