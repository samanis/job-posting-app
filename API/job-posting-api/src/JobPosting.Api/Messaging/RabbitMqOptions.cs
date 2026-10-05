using Microsoft.Extensions.Options;

namespace JobPosting.Api.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string Exchange { get; set; } = "job-post-exchange";
    public string Queue { get; set; } = "job-post-queue";
    public string RoutingKey { get; set; } = "job-posting.created.v1";
    public int PublishBudgetSeconds { get; set; } = 8;
    public int ConfirmTimeoutSeconds { get; set; } = 3;
}

public sealed class RabbitMqOptionsValidator : IValidateOptions<RabbitMqOptions>
{
    public ValidateOptionsResult Validate(string? name, RabbitMqOptions options)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(options.HostName)) errors.Add("RabbitMq:HostName is required.");
        if (options.Port is < 1 or > 65535) errors.Add("RabbitMq:Port must be between 1 and 65535.");
        if (string.IsNullOrWhiteSpace(options.UserName)) errors.Add("RabbitMq:UserName is required.");
        if (string.IsNullOrEmpty(options.Password)) errors.Add("RabbitMq:Password is required.");
        if (string.IsNullOrWhiteSpace(options.VirtualHost)) errors.Add("RabbitMq:VirtualHost is required.");
        foreach (var value in new[] { options.Exchange, options.Queue, options.RoutingKey })
            if (string.IsNullOrWhiteSpace(value) || System.Text.Encoding.UTF8.GetByteCount(value) > 255 || value.StartsWith("amq.", StringComparison.Ordinal))
                errors.Add("RabbitMq topology names must be nonempty, at most 255 UTF-8 bytes and not reserved.");
        if (options.PublishBudgetSeconds is < 1 or > 10) errors.Add("RabbitMq:PublishBudgetSeconds must be between 1 and 10.");
        if (options.ConfirmTimeoutSeconds is < 1 or > 3 || options.ConfirmTimeoutSeconds > options.PublishBudgetSeconds)
            errors.Add("RabbitMq:ConfirmTimeoutSeconds must be between 1 and 3 and within the publish budget.");
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }
}
