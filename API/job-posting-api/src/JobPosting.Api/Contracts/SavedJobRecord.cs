namespace JobPosting.Api.Contracts;

public class SavedJobRecord
{
    public required string Id { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Title { get; init; }
    public required string Department { get; init; }
    public required string Location { get; init; }
    public required string Description { get; init; }
    public required decimal SalaryMin { get; init; }
    public required decimal SalaryMax { get; init; }
    public required DateOnly ClosingDate { get; init; }

    // The later persistence stage supplies the generated identity and injectable creation time.
    public static SavedJobRecord Create(NormalizedJobRequest request, Guid id, DateTimeOffset createdAt) => new()
    {
        Id = id.ToString("D"),
        CreatedAt = createdAt.ToUniversalTime(),
        Title = request.Title,
        Department = request.Department,
        Location = request.Location,
        Description = request.Description,
        SalaryMin = request.SalaryMin,
        SalaryMax = request.SalaryMax,
        ClosingDate = request.ClosingDate
    };
}
