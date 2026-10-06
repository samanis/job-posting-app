using JobSearch.Api.Contracts;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
namespace JobSearch.Api.Messaging;

public static class SearchMessaging
{
    public static void AddSearchMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<ConsumerOptions>, ConsumerOptionsValidator>();
        services.AddOptions<ConsumerOptions>().Bind(configuration.GetSection("RabbitMq")).ValidateOnStart();
        services.AddSingleton<IConnectionFactory>(p => CreateFactory(p.GetRequiredService<IOptions<ConsumerOptions>>().Value));
        services.AddSingleton<IngestionState>();
        services.AddSingleton<IBackoffJitter, BackoffJitter>();
        services.AddSingleton<IQuarantinePublisher, RabbitMqQuarantinePublisher>();
        services.AddSingleton<EventReader>();
        services.AddScoped<ISearchEventHandler, SearchEventHandler>();
        services.AddSingleton<IConsumerSession, RabbitMqConsumerSession>();
        services.AddHostedService<SearchConsumerWorker>();
    }

    public static ConnectionFactory CreateFactory(ConsumerOptions o) => new()
    {
        HostName = o.HostName,
        Port = o.Port,
        UserName = o.UserName,
        Password = o.Password,
        VirtualHost = o.VirtualHost,
        ClientProvidedName = "job-search-api",
        AutomaticRecoveryEnabled = false,
        TopologyRecoveryEnabled = false,
        ConsumerDispatchConcurrency = (ushort)o.Concurrency,
        RequestedConnectionTimeout = TimeSpan.FromSeconds(3),
        HandshakeContinuationTimeout = TimeSpan.FromSeconds(3),
        ContinuationTimeout = TimeSpan.FromSeconds(3),
        SocketReadTimeout = TimeSpan.FromSeconds(3),
        SocketWriteTimeout = TimeSpan.FromSeconds(3)
    };
}
