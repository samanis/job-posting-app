using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
namespace JobSearch.Api.Contracts;
public static class EventFingerprint
{
    public const int Version = 1;
    public static string Compute(JobCreatedEvent e)
    {
        var j = e.Job;
        // Transport correlation is diagnostic and is not durable projection identity.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new object[] { Version, e.EventId.ToString("D"), e.OccurredAt.ToString("O", CultureInfo.InvariantCulture),
            j.Id.ToString("D"), j.CreatedAt.ToString("O", CultureInfo.InvariantCulture), j.Title, j.Department, j.Location, j.Description,
            j.SalaryMin.ToString("F2", CultureInfo.InvariantCulture), j.SalaryMax.ToString("F2", CultureInfo.InvariantCulture), j.ClosingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
