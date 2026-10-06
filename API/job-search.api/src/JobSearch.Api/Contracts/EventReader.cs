using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace JobSearch.Api.Contracts;

public sealed class EventReader
{
    private static readonly string[] EnvelopeFields = ["eventId", "eventType", "schemaVersion", "occurredAt", "correlationId", "job"];
    private static readonly string[] JobFields = ["id", "createdAt", "title", "department", "location", "description", "salaryMin", "salaryMax", "closingDate"];
    public EventReadResult Read(ReadOnlyMemory<byte> body, int maximumBytes, EventMetadata metadata)
    {
        if (body.Length > maximumBytes) return new(null, "event_too_large");
        try
        {
            // Throw on invalid UTF-8 instead of replacing bytes.
            var text = new UTF8Encoding(false, true).GetString(body.Span);
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            var e = document.RootElement;
            Shape(e, EnvelopeFields);
            if (e.GetProperty("schemaVersion").GetInt32() != 1) return new(null, "unsupported_version");
            if (Text(e, "eventType", 100) != "JobPostingCreated") throw new FormatException();
            var eventId = Id(e, "eventId");
            var occurred = Timestamp(e, "occurredAt");
            var correlation = Text(e, "correlationId", 256);
            var j = e.GetProperty("job");
            Shape(j, JobFields);
            var job = new ProjectedJob(Id(j, "id"), Timestamp(j, "createdAt"), Text(j, "title", 200),
                Text(j, "department", 100), Text(j, "location", 100), Text(j, "description", 10000),
                Money(j, "salaryMin"), Money(j, "salaryMax"), Date(j));
            if (job.SalaryMin >= job.SalaryMax) throw new FormatException();
            if (metadata.MessageId is not null && metadata.MessageId != eventId.ToString("D")) throw new FormatException();
            if (metadata.Type is not null && metadata.Type != "JobPostingCreated") throw new FormatException();
            if (metadata.SchemaVersion is not null && metadata.SchemaVersion != 1) throw new FormatException();
            if (metadata.ContentType is not null && metadata.ContentType != "application/json") throw new FormatException();
            return new(new(eventId, occurred, correlation, job), null);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException or KeyNotFoundException or DecoderFallbackException or OverflowException)
        {
            return new(null, "invalid_event");
        }
    }
    private static void Shape(JsonElement element, string[] fields)
    {
        if (element.ValueKind != JsonValueKind.Object) throw new FormatException();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in element.EnumerateObject())
        {
            if (!seen.Add(p.Name)) throw new FormatException();
            if (fields.Any(f => f.Equals(p.Name, StringComparison.OrdinalIgnoreCase)) && !fields.Contains(p.Name)) throw new FormatException();
        }
    }
    private static string Text(JsonElement e, string field, int maximum)
    {
        var value = e.GetProperty(field).GetString();
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum) throw new FormatException();
        return value;
    }
    private static Guid Id(JsonElement e, string field)
    {
        if (!Guid.TryParseExact(Text(e, field, 36), "D", out var id) || id == Guid.Empty) throw new FormatException();
        return id;
    }
    private static DateTimeOffset Timestamp(JsonElement e, string field)
    {
        var value = Text(e, field, 40);
        if (!Regex.IsMatch(value, @"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d{1,7})?(Z|[+-]\d{2}:\d{2})$") ||
            !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp)) throw new FormatException();
        return timestamp.ToUniversalTime();
    }
    private static DateOnly Date(JsonElement e)
    {
        var value = Text(e, "closingDate", 10);
        if (!DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new FormatException();
        return date;
    }
    private static decimal Money(JsonElement e, string field)
    {
        var token = e.GetProperty(field);
        if (token.ValueKind != JsonValueKind.Number || !token.TryGetDecimal(out var value)) throw new FormatException();
        var parts = token.GetRawText().ToLowerInvariant().Split('e');
        var mantissa = parts[0];
        var exponent = parts.Length == 2 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        var dot = mantissa.IndexOf('.');
        var scale = dot < 0 ? 0L : mantissa.Length - dot - 1L;
        if (scale - exponent > 2 || value < 0 || value > 999999999.99m) throw new FormatException();
        return value;
    }
}
