namespace JobPosting.Api.Contracts;

// Missing/null salaries remain distinguishable from a legitimate zero.
// Incoming JSON must go through CreateJobRequestReader, not permissive MVC numeric binding.
public sealed class CreateJobRequest
{
    public string? Title { get; init; }
    public string? Department { get; init; }
    public string? Location { get; init; }
    public string? Description { get; init; }
    public decimal? SalaryMin { get; init; }
    public decimal? SalaryMax { get; init; }
    public DateOnly? ClosingDate { get; init; }
}
