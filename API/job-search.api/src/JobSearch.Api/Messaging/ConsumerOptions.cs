using System.Text;
using Microsoft.Extensions.Options;
namespace JobSearch.Api.Messaging;

public sealed class ConsumerOptions
{
    public bool Enabled { get; set; } = true;
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "job-post-exchange";
    public string Queue { get; set; } = "job-post-queue";
    public string RoutingKey { get; set; } = "job-posting.created.v1";
    public string QuarantineExchange { get; set; } = "job-search-quarantine-exchange";
    public string QuarantineQueue { get; set; } = "job-search-quarantine-queue";
    public string QuarantineRoutingKey { get; set; } = "job-search.rejected.v1";
    public int QuarantineBudgetSeconds { get; set; } = 8;
    public int DrainSeconds { get; set; } = 24;
    public int ReconnectInitialSeconds { get; set; } = 1;
    public int ReconnectMaximumSeconds { get; set; } = 30;
    public int Prefetch { get; set; } = 10;
    public int Concurrency { get; set; } = 1;
}
public sealed class ConsumerOptionsValidator : IValidateOptions<ConsumerOptions>
{
    public ValidateOptionsResult Validate(string? name, ConsumerOptions o)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(o.HostName)) errors.Add("RabbitMq:HostName is required.");
        if (o.Port is < 1 or > 65535) errors.Add("RabbitMq:Port must be 1..65535.");
        if (string.IsNullOrWhiteSpace(o.UserName)) errors.Add("RabbitMq:UserName is required.");
        if (string.IsNullOrWhiteSpace(o.Password)) errors.Add("RabbitMq:Password is required.");
        if (string.IsNullOrWhiteSpace(o.VirtualHost)) errors.Add("RabbitMq:VirtualHost is required.");
        foreach (var item in new[] { ("Exchange", o.Exchange), ("Queue", o.Queue), ("RoutingKey", o.RoutingKey), ("QuarantineExchange", o.QuarantineExchange), ("QuarantineQueue", o.QuarantineQueue), ("QuarantineRoutingKey", o.QuarantineRoutingKey) })
            if (string.IsNullOrWhiteSpace(item.Item2) || Encoding.UTF8.GetByteCount(item.Item2) > 255 || item.Item2.StartsWith("amq.", StringComparison.Ordinal)) errors.Add("RabbitMq:" + item.Item1 + " must be a valid nonreserved name.");
        if (o.Prefetch is < 1 or > 100) errors.Add("RabbitMq:Prefetch must be 1..100.");
        if (o.Concurrency is < 1 or > 10 || o.Concurrency > o.Prefetch) errors.Add("RabbitMq:Concurrency must be 1..10 and no greater than Prefetch.");
        if (o.QuarantineExchange == o.Exchange || o.QuarantineQueue == o.Queue) errors.Add("RabbitMq:Quarantine names must be separate from the source topology.");
        if (o.QuarantineBudgetSeconds is < 1 or > 10) errors.Add("RabbitMq:QuarantineBudgetSeconds must be 1..10.");
        if (o.DrainSeconds is < 1 or > 24) errors.Add("RabbitMq:DrainSeconds must be 1..24, reserving cleanup within the 30-second host window.");
        if (o.ReconnectInitialSeconds is < 1 or > 30) errors.Add("RabbitMq:ReconnectInitialSeconds must be 1..30.");
        if (o.ReconnectMaximumSeconds is < 1 or > 30 || o.ReconnectMaximumSeconds < o.ReconnectInitialSeconds) errors.Add("RabbitMq:ReconnectMaximumSeconds must be initial..30.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
