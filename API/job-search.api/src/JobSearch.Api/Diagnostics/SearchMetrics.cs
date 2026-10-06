using System.Diagnostics.Metrics;
using JobSearch.Api.Messaging;
namespace JobSearch.Api.Diagnostics;

public static class SearchMetrics
{
    public const string MeterName = "JobSearch.Api";
    private static readonly Meter Meter = new(MeterName, "1.0");
    private static readonly Counter<long> Projections = Meter.CreateCounter<long>("search.projections");
    private static readonly Counter<long> Redeliveries = Meter.CreateCounter<long>("search.redeliveries");
    private static readonly Counter<long> Quarantines = Meter.CreateCounter<long>("search.quarantines");
    private static readonly Counter<long> Connections = Meter.CreateCounter<long>("search.consumer.transitions");
    private static readonly Histogram<double> Requests = Meter.CreateHistogram<double>("search.request.duration", "ms");
    public static void Projection(DeliveryOutcome outcome) => Projections.Add(1, new KeyValuePair<string, object?>("outcome", outcome.ToString()));
    public static void Redelivery() => Redeliveries.Add(1);
    public static void Quarantine(string outcome) => Quarantines.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
    public static void Connection(ConsumerState state) => Connections.Add(1, new KeyValuePair<string, object?>("state", state.ToString()));
    public static void Request(int status, double milliseconds) => Requests.Record(milliseconds, new KeyValuePair<string, object?>("status_class", status / 100));
}
