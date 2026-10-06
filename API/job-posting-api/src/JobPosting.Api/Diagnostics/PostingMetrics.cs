using System.Diagnostics.Metrics;
namespace JobPosting.Api.Diagnostics;

public static class PostingMetrics
{
    public const string MeterName = "JobPosting.Api";
    private static readonly Meter Meter = new(MeterName, "1.0.0");
    private static readonly Counter<long> Requests = Meter.CreateCounter<long>("jobposting.requests");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("jobposting.request.duration", "ms");
    private static readonly Counter<long> Publications = Meter.CreateCounter<long>("jobposting.publications");
    private static readonly Counter<long> Compensation = Meter.CreateCounter<long>("jobposting.compensations");
    private static readonly Counter<long> Breaker = Meter.CreateCounter<long>("jobposting.breaker.transitions");
    public static void Request(int status, double milliseconds) { var tag = new KeyValuePair<string, object?>("status_class", status / 100); Requests.Add(1, tag); Duration.Record(milliseconds, tag); }
    public static void Publication(string outcome) => Publications.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    public static void Cleanup(string outcome) => Compensation.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    public static void Transition(string state) => Breaker.Add(1, new KeyValuePair<string, object?>("state", state));
}
