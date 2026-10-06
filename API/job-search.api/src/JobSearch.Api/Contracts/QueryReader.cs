using System.Globalization;
using Microsoft.Extensions.Primitives;
namespace JobSearch.Api.Contracts;
public sealed class QueryReader(TimeProvider clock)
{
    public DateOnly TodayUtc() => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    public static bool Available(DateOnly closingDate, DateOnly today) => closingDate > today;
    public static bool TryId(string value, out Guid id) => Guid.TryParseExact(value, "D", out id) && id != Guid.Empty;
    public QueryReadResult Read(IEnumerable<KeyValuePair<string, StringValues>> parameters)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in parameters)
        {
            if (!new[] { "q", "department", "location", "limit", "sort", "cursor" }.Contains(p.Key) || p.Value.Count != 1 || !values.TryAdd(p.Key, p.Value.ToString())) return new(null, "invalid_query");
        }
        try
        {
            var q = Filter(values.GetValueOrDefault("q", ""), 200);
            var department = Filter(values.GetValueOrDefault("department", ""), 100);
            var location = Filter(values.GetValueOrDefault("location", ""), 100);
            var limit = 20;
            if (values.TryGetValue("limit", out var raw) && (!int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > 50)) throw new FormatException();
            var sort = values.GetValueOrDefault("sort", "newest");
            if (sort is not ("newest" or "closing-soon")) throw new FormatException();
            var cursor = values.GetValueOrDefault("cursor");
            if (cursor is not null && (cursor.Length is 0 or > 2048 || cursor.Any(char.IsWhiteSpace) || cursor.Any(char.IsControl))) throw new FormatException();
            return new(new(q, department, location, limit, sort, cursor), null);
        }
        catch (FormatException) { return new(null, "invalid_query"); }
    }
    private static string? Filter(string value, int maximum)
    {
        var trimmed = value.Trim();
        if (trimmed.Length > maximum || trimmed.Any(char.IsControl)) throw new FormatException();
        return trimmed.Length == 0 ? null : trimmed;
    }
}
