using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace JobPosting.Api.Messaging;

public static class RabbitMqMessaging
{
    public static void AddJobMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<RabbitMqOptions>, RabbitMqOptionsValidator>();
        services.AddOptions<RabbitMqOptions>().Bind(configuration.GetSection(RabbitMqOptions.SectionName)).ValidateOnStart();
        services.AddSingleton<IConnectionFactory>(provider => CreateFactory(provider.GetRequiredService<IOptions<RabbitMqOptions>>().Value));
        services.AddSingleton<RabbitMqJobEventPublisher>();
        services.AddSingleton<JobPosting.Api.Resilience.PublicationCircuit>(provider => new(
            provider.GetRequiredService<RabbitMqJobEventPublisher>(), provider.GetRequiredService<RabbitMqJobEventPublisher>(),
            provider.GetRequiredService<IOptions<JobPosting.Api.Resilience.ResilienceOptions>>(),
            provider.GetRequiredService<TimeProvider>(), provider.GetRequiredService<ILogger<JobPosting.Api.Resilience.PublicationCircuit>>()));
        services.AddSingleton<IJobEventPublisher>(provider => provider.GetRequiredService<JobPosting.Api.Resilience.PublicationCircuit>());
        services.AddSingleton<JobPosting.Api.Resilience.IPublisherProbe>(provider => provider.GetRequiredService<JobPosting.Api.Resilience.PublicationCircuit>());
    }

    public static ConnectionFactory CreateFactory(RabbitMqOptions options) => new()
    {
        HostName = options.HostName, Port = options.Port, UserName = options.UserName, Password = options.Password,
        VirtualHost = options.VirtualHost, ClientProvidedName = "job-posting-api",
        // Connection repair happens on the next invocation; the library must not replay publications.
        AutomaticRecoveryEnabled = false, TopologyRecoveryEnabled = false,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(3), HandshakeContinuationTimeout = TimeSpan.FromSeconds(3),
        ContinuationTimeout = TimeSpan.FromSeconds(3), SocketReadTimeout = TimeSpan.FromSeconds(3), SocketWriteTimeout = TimeSpan.FromSeconds(3)
    };
}
