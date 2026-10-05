namespace JobPosting.Api.Contracts;

// Constructed only after non-temporal validation succeeds. No mutable payload survives normalization.
public sealed class NormalizedJobRequest
{
    internal NormalizedJobRequest(string title, string department, string location, string description,
        decimal salaryMin, decimal salaryMax, DateOnly closingDate)
    {
        Title = title;
        Department = department;
        Location = location;
        Description = description;
        SalaryMin = salaryMin;
        SalaryMax = salaryMax;
        ClosingDate = closingDate;
    }

    public string Title { get; }
    public string Department { get; }
    public string Location { get; }
    public string Description { get; }
    public decimal SalaryMin { get; }
    public decimal SalaryMax { get; }
    public DateOnly ClosingDate { get; }
}
