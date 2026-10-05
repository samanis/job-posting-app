namespace JobPosting.Api.Persistence;

public sealed class JobPostingEntity
{
    public Guid Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public required string Title { get; set; }
    public required string Department { get; set; }
    public required string Location { get; set; }
    public required string Description { get; set; }
    public decimal SalaryMin { get; set; }
    public decimal SalaryMax { get; set; }
    public DateOnly ClosingDate { get; set; }
    public required string IdempotencyKeyDigest { get; set; }
    public required string RequestFingerprint { get; set; }
    public int CanonicalizationVersion { get; set; }
    public required string ResponseJson { get; set; }
    public Guid EventId { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
}
