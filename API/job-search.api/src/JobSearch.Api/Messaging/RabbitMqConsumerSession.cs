using JobSearch.Api.Contracts;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
namespace JobSearch.Api.Messaging;

public sealed class RabbitMqConsumerSession(
    IConnectionFactory factory,
    IOptions<ConsumerOptions> options,
    IServiceScopeFactory scopes,
    ILogger<RabbitMqConsumerSession> logger,
    IQuarantinePublisher quarantine,
    IngestionState state,
    TimeProvider clock) : IConsumerSession
{
    public async Task RunAsync(CancellationToken token)
    {
        var o = options.Value;
        using var lifetime = new CancellationTokenSource();
        var processingToken = lifetime.Token;
        var drain = new SessionDrain();
        string? consumerTag = null;
        var closing = 0;
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        IConnection? connection = null;
        IChannel? channel = null;
        var settlements = new SemaphoreSlim(1);
        var processing = new SemaphoreSlim(o.Concurrency);
        try
        {
            connection = await factory.CreateConnectionAsync(token);
            channel = await connection.CreateChannelAsync(
                new CreateChannelOptions(false, false, consumerDispatchConcurrency: (ushort)o.Concurrency),
                token);
            var owner = channel; // Never replace the channel captured by a callback/tag.
            channel.ChannelShutdownAsync += (_, _) =>
            {
                if (Volatile.Read(ref closing) == 0) failure.TrySetResult(new IOException("Consumer channel closed."));
                return Task.CompletedTask;
            };
            await channel.ExchangeDeclareAsync(o.Exchange, ExchangeType.Direct, true, false, cancellationToken: token);
            await channel.QueueDeclareAsync(o.Queue, true, false, false, cancellationToken: token);
            await channel.QueueBindAsync(o.Queue, o.Exchange, o.RoutingKey, cancellationToken: token);
            await channel.BasicQosAsync(0, (ushort)o.Prefetch, false, token);
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.UnregisteredAsync += (_, _) =>
            {
                if (Volatile.Read(ref closing) == 0) failure.TrySetResult(new IOException("Consumer subscription ended."));
                return Task.CompletedTask;
            };
            consumer.ReceivedAsync += async (_, delivery) =>
            {
                if (!drain.TryEnter()) return;
                try
                {
                    await processing.WaitAsync(processingToken);
                    try
                    {
                        // Body is processed within callback lifetime: no borrowed memory escapes.
                        if (delivery.Redelivered) JobSearch.Api.Diagnostics.SearchMetrics.Redelivery();
                        try
                        {
                            var metadata = Metadata(delivery.BasicProperties);
                            await using var scope = scopes.CreateAsyncScope();
                            var result = await scope.ServiceProvider.GetRequiredService<ISearchEventHandler>()
                                .HandleAsync(delivery.Body, metadata, processingToken);
                            JobSearch.Api.Diagnostics.SearchMetrics.Projection(result);
                        }
                        catch (PermanentDeliveryException ex)
                        {
                            await quarantine.PublishAsync(
                                new(
                                    delivery.Body,
                                    ex.Code,
                                    delivery.BasicProperties.MessageId,
                                    delivery.BasicProperties.CorrelationId,
                                    delivery.BasicProperties.ContentType),
                                processingToken);
                            logger.LogWarning(
                                new EventId(4201, "DeliveryQuarantined"),
                                "Delivery quarantined; reason {Reason}",
                                ex.Code);
                        }
                        await settlements.WaitAsync(processingToken);
                        try
                        {
                            processingToken.ThrowIfCancellationRequested();
                            if (!owner.IsOpen) throw new IOException("Delivery channel closed.");
                            await owner.BasicAckAsync(delivery.DeliveryTag, false, processingToken);
                        }
                        finally { settlements.Release(); }
                    }
                    finally { processing.Release(); }
                }
                catch (Exception ex) { failure.TrySetResult(ex); }
                finally { drain.Exit(); }
            };
            consumerTag = await channel.BasicConsumeAsync(o.Queue, false, consumer, token);
            state.Set(ConsumerState.Running);
            throw await failure.Task.WaitAsync(token);
        }
        finally
        {
            Interlocked.Exchange(ref closing, 1);
            var pending = drain.Stop();
            if (token.IsCancellationRequested)
            {
                state.Set(ConsumerState.Draining);
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(o.DrainSeconds), clock);
                try
                {
                    if (channel is not null && consumerTag is not null && channel.IsOpen)
                        await channel.BasicCancelAsync(consumerTag, true, budget.Token).WaitAsync(budget.Token);
                    await pending.WaitAsync(budget.Token);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(
                        new EventId(4103, "DrainIncomplete"),
                        "Consumer drain incomplete; failure {Failure}",
                        ex.GetType().Name);
                }
            }
            await lifetime.CancelAsync();
            if (channel is not null) await DisposeResourceAsync(channel);
            if (connection is not null) await DisposeResourceAsync(connection);
        }
    }

    private async Task DisposeResourceAsync(IAsyncDisposable resource)
    {
        try { await resource.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)); }
        catch (Exception ex)
        {
            logger.LogWarning(
                new EventId(4102, "ConsumerDisposeFailed"),
                "Consumer resource cleanup failed; failure {Failure}",
                ex.GetType().Name);
        }
    }

    public static EventMetadata Metadata(IReadOnlyBasicProperties properties)
    {
        int? version = null;
        if (properties.Headers is not null && properties.Headers.TryGetValue("schemaVersion", out var raw))
            version = raw switch
            {
                byte v => v,
                sbyte v => v,
                short v => v,
                ushort v => v,
                int v => v,
                long v when v is >= int.MinValue and <= int.MaxValue => (int)v,
                _ => throw new PermanentDeliveryException("invalid_metadata")
            };
        return new(properties.MessageId, properties.Type, version, properties.ContentType);
    }
}
