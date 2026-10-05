using JobPosting.Api.Contracts;

namespace JobPosting.Api.Validation;

public sealed class JobRequestValidator
{
    public static decimal MaximumSalary => 999_999_999.99m;

    public JobRequestValidationResult Normalize(CreateJobRequest request)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var title = Text(request.Title, "title", 200, errors);
        var department = Text(request.Department, "department", 100, errors);
        var location = Text(request.Location, "location", 100, errors);
        var description = Text(request.Description, "description", 10_000, errors);
        Salary(request.SalaryMin, "salaryMin", errors);
        Salary(request.SalaryMax, "salaryMax", errors);
        if (request.SalaryMin.HasValue && request.SalaryMax.HasValue && request.SalaryMin >= request.SalaryMax)
            errors["salaryMax"] = ["The maximum salary must be greater than the minimum salary."];
        if (!request.ClosingDate.HasValue) errors["closingDate"] = ["The closing date is required."];
        if (errors.Count > 0) return new(null, errors);
        return new(new NormalizedJobRequest(title!, department!, location!, description!,
            request.SalaryMin!.Value, request.SalaryMax!.Value, request.ClosingDate!.Value), errors);
    }

    private static string? Text(string? value, string name, int maximum, Dictionary<string, string[]> errors)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized)) errors[name] = ["The field is required and must not be blank."];
        else if (normalized.Length > maximum) errors[name] = [$"The field must contain at most {maximum} characters after trimming."];
        return normalized;
    }

    private static void Salary(decimal? value, string name, Dictionary<string, string[]> errors)
    {
        if (!value.HasValue) errors[name] = ["The salary is required."];
        else if (value < 0 || value > MaximumSalary) errors[name] = ["The salary must be between 0 and 999999999.99."];
        else if (((decimal.GetBits(value.Value)[3] >> 16) & 0xff) > 2) errors[name] = ["The salary must have at most two decimal places."];
    }
}
