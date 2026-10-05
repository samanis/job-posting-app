using System.Globalization;
using System.Text.Json;
using JobPosting.Api.Contracts;

namespace JobPosting.Api.Validation;

public sealed class CreateJobRequestReader
{
    private static readonly HashSet<string> Fields = new(StringComparer.Ordinal)
    {
        "title", "department", "location", "description", "salaryMin", "salaryMax", "closingDate"
    };

    public RequestReadResult Read(string json)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var status = 422;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(null, 400, new Dictionary<string, string[]> { ["$"] = ["The request must be a JSON object."] });
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in root.EnumerateObject())
            {
                if (!Fields.Contains(field.Name))
                {
                    errors["$"] = ["The request contains unsupported fields. Use the documented camelCase names."];
                    status = 400;
                }
                else if (!seen.Add(field.Name))
                {
                    errors[field.Name] = ["Each field must occur only once."];
                    status = 400;
                }
            }
            var request = new CreateJobRequest
            {
                Title = Text(root, "title", errors, ref status),
                Department = Text(root, "department", errors, ref status),
                Location = Text(root, "location", errors, ref status),
                Description = Text(root, "description", errors, ref status),
                SalaryMin = Salary(root, "salaryMin", errors, ref status),
                SalaryMax = Salary(root, "salaryMax", errors, ref status),
                ClosingDate = Date(root, errors, ref status)
            };
            return errors.Count == 0 ? new(request, null, errors) : new(null, status, errors);
        }
        catch (JsonException)
        {
            return InvalidJson();
        }
        catch (InvalidOperationException)
        {
            // JsonDocument accepts escaped surrogate tokens, but string/name decoding rejects invalid UTF-16.
            // All ValueKind access is checked above; decoding failures are invalid input, not server diagnostics.
            return InvalidJson();
        }
    }

    private static RequestReadResult InvalidJson() =>
        new(null, 400, new Dictionary<string, string[]> { ["$"] = ["The request must contain valid JSON."] });

    private static string? Text(JsonElement root, string name, Dictionary<string, string[]> errors, ref int status)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        errors[name] = ["The field must be a JSON string."];
        status = 400;
        return null;
    }

    private static decimal? Salary(JsonElement root, string name, Dictionary<string, string[]> errors, ref int status)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount))
        {
            errors[name] = ["The field must be a representable decimal JSON number, not a string."];
            status = 400;
            return null;
        }
        // Examine the original token: decimal parsing can otherwise round a tiny excess fraction to zero.
        if (HasExcessPrecision(value.GetRawText()))
        {
            errors[name] = ["The salary must have at most two decimal places."];
            return null;
        }
        return amount;
    }

    private static bool HasExcessPrecision(string token)
    {
        var exponentIndex = token.IndexOfAny(['e', 'E']);
        var mantissaLength = exponentIndex < 0 ? token.Length : exponentIndex;
        var dot = token.IndexOf('.');
        var fractionalDigits = dot < 0 ? 0 : mantissaLength - dot - 1;
        if (exponentIndex < 0) return fractionalDigits > 2;
        if (!long.TryParse(token.AsSpan(exponentIndex + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var exponent))
            return token[exponentIndex + 1] == '-';
        // This comparison avoids overflow even for long.MinValue exponents.
        return exponent < fractionalDigits - 2;
    }

    private static DateOnly? Date(JsonElement root, Dictionary<string, string[]> errors, ref int status)
    {
        var text = Text(root, "closingDate", errors, ref status);
        if (text is null) return null;
        if (text.Length == 10 && DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        errors["closingDate"] = ["The closing date must be a valid calendar date in YYYY-MM-DD format."];
        status = 400;
        return null;
    }
}
