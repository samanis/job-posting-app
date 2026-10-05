using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using JobPosting.Api.Contracts;

namespace JobPosting.Api.Validation;

public static class JobRequestFingerprint
{
    public const int Version = 1;

    public static string Compute(NormalizedJobRequest request) => Convert.ToHexString(SHA256.HashData(Canonicalize(request))).ToLowerInvariant();

    public static byte[] Canonicalize(NormalizedJobRequest request)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            writer.WriteNumberValue(Version);
            writer.WriteStringValue(request.Title);
            writer.WriteStringValue(request.Department);
            writer.WriteStringValue(request.Location);
            writer.WriteStringValue(request.Description);
            writer.WriteStringValue(request.SalaryMin.ToString("0.00", CultureInfo.InvariantCulture));
            writer.WriteStringValue(request.SalaryMax.ToString("0.00", CultureInfo.InvariantCulture));
            writer.WriteStringValue(request.ClosingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            writer.WriteEndArray();
        }
        return buffer.ToArray();
    }
}
