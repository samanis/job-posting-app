namespace JobPosting.Api.Contracts;

public sealed class JobAcceptedResponse : SavedJobRecord
{
    public string Status => "accepted";
    public string Message => "Your job posting has been saved and sent for processing. It may take a few moments to appear in search results.";

    // This builds a contract, not evidence of persistence/publication. Only the later workflow may return 202.
    public static JobAcceptedResponse FromSaved(SavedJobRecord saved) => new()
    {
        Id = saved.Id,
        CreatedAt = saved.CreatedAt,
        Title = saved.Title,
        Department = saved.Department,
        Location = saved.Location,
        Description = saved.Description,
        SalaryMin = saved.SalaryMin,
        SalaryMax = saved.SalaryMax,
        ClosingDate = saved.ClosingDate
    };
}
