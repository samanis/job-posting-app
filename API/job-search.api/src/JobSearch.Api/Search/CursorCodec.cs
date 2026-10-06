using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;
using JobSearch.Api.Contracts;
namespace JobSearch.Api.Search;

public sealed record PagePosition(long CreatedTicks, Guid Id, int ClosingDay);

public sealed record CursorPayload(
    int Version,
    string KeyId,
    string Binding,
    int Day,
    long Watermark,
    PagePosition Last,
    long IssuedTicks,
    long ExpiryTicks);

public sealed record CursorResult(CursorPayload? Payload, string? Error);

public sealed class CursorCodec(IOptions<CursorOptions> options)
{
    private static readonly string[] Fields =
        ["Version", "KeyId", "Binding", "Day", "Watermark", "Last", "IssuedTicks", "ExpiryTicks"];

    public static string Binding(SearchQuery query) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[]
    {
        query.Q,
        query.Department,
        query.Location,
        query.Limit.ToString(System.Globalization.CultureInfo.InvariantCulture),
        query.Sort
    })));

    public CursorPayload Start(SearchQuery query, DateTimeOffset now, long watermark) => new(
        1,
        options.Value.ActiveKeyId,
        Binding(query),
        DateOnly.FromDateTime(now.UtcDateTime).DayNumber,
        watermark,
        new(0, Guid.Empty, 0),
        now.UtcTicks,
        now.AddMinutes(options.Value.LifetimeMinutes).UtcTicks);

    public string Encode(CursorPayload payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
        var mac = HMACSHA256.HashData(Convert.FromBase64String(options.Value.Keys[payload.KeyId]), bytes);
        return Url(bytes) + "." + Url(mac);
    }

    public CursorResult Decode(string cursor, SearchQuery query, DateTimeOffset now)
    {
        if (cursor.Length is 0 or > 2048) return new(null, "invalid_cursor");
        try
        {
            var parts = cursor.Split('.');
            if (parts.Length != 2) return new(null, "invalid_cursor");
            var body = Unurl(parts[0]);
            var mac = Unurl(parts[1]);
            using var json = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 4 });
            var root = json.RootElement;
            if (!Shape(root, Fields) || !Shape(root.GetProperty("Last"), ["CreatedTicks", "Id", "ClosingDay"]))
                return new(null, "invalid_cursor");
            var p = JsonSerializer.Deserialize<CursorPayload>(body)!;
            if (p.KeyId is null
                || !options.Value.Keys.TryGetValue(p.KeyId, out var secret)
                || !CryptographicOperations.FixedTimeEquals(mac, HMACSHA256.HashData(Convert.FromBase64String(secret), body)))
                return new(null, "invalid_cursor");
            if (p.Binding != Binding(query)
                || p.Watermark < 0
                || p.Day is < 0 or > 3652058
                || p.Last.ClosingDay is < 0 or > 3652058
                || p.Last.Id == Guid.Empty
                || p.Last.CreatedTicks is < 0 or > 3155378975999999999
                || p.IssuedTicks < 0
                || p.IssuedTicks > now.UtcTicks
                || p.ExpiryTicks <= p.IssuedTicks
                || p.ExpiryTicks - p.IssuedTicks > TimeSpan.FromHours(1).Ticks
                || p.ExpiryTicks > 3155378975999999999)
                return new(null, "invalid_cursor");
            return p.Version != 1 || now.UtcTicks >= p.ExpiryTicks ? new(null, "cursor_expired") : new(p, null);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or OverflowException)
        {
            return new(null, "invalid_cursor");
        }
    }

    private static bool Shape(JsonElement element, string[] fields) =>
        element.ValueKind == JsonValueKind.Object
        && element.EnumerateObject().Select(p => p.Name).Order().SequenceEqual(fields.Order());

    private static string Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Unurl(string value)
    {
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')) throw new FormatException();
        var bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
        if (Url(bytes) != value) throw new FormatException();
        return bytes;
    }
}
