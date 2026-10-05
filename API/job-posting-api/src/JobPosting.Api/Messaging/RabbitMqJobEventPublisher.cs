using System.Text.Json;
using System.Diagnostics;
using JobPosting.Api.Resilience;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace JobPosting.Api.Messaging;

public sealed class RabbitMqJobEventPublisher(IConnectionFactory factory, IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqJobEventPublisher> logger) : IJobEventPublisher, IPublisherProbe, IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    private IChannel? channel;
    private bool disposed;

    public async Task PublishAsync(JobPostingCreated envelope, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var started = Stopwatch.GetTimestamp();
        var body = JsonSerializer.SerializeToUtf8Bytes(envelope, JsonOptions);
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(settings.PublishBudgetSeconds));
        var acquired = false;
        var sendStarted = false;
        try
        {
            await gate.WaitAsync(budget.Token);
            acquired = true;
            ObjectDisposedException.ThrowIf(disposed, this);
            await EnsureChannelAsync(settings, started, budget.Token);
            var properties = new BasicProperties
            {
                Persistent = true, ContentType = "application/json", ContentEncoding = "utf-8",
                MessageId = envelope.EventId.ToString("D"), CorrelationId = envelope.CorrelationId,
                Type = envelope.EventType, Timestamp = new AmqpTimestamp(envelope.OccurredAt.ToUnixTimeSeconds()),
                Headers = new Dictionary<string, object?> { ["schemaVersion"] = envelope.SchemaVersion }
            };
            using var confirmation = CancellationTokenSource.CreateLinkedTokenSource(budget.Token);
            confirmation.CancelAfter(TimeSpan.FromSeconds(settings.ConfirmTimeoutSeconds));
            sendStarted = true;
            // With both confirmation options enabled, this awaits ack and rejects nack/basic.return.
            await channel!.BasicPublishAsync(settings.Exchange, settings.RoutingKey, mandatory: true, properties, body, confirmation.Token).AsTask().WaitAsync(confirmation.Token);
        }
        catch (Exception exception)
        {
            // Never touch another invocation's resources when cancelled while queued for the gate.
            if (acquired) await ResetAsync(Remaining(started, settings));
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            var failure = sendStarted && exception is not PublishException
                ? PublicationFailure.AcceptanceUnknown : PublicationFailure.NotAccepted;
            throw new JobPublicationException(failure, exception);
        }
        finally
        {
            if (acquired) gate.Release();
        }
    }

    private static TimeSpan Remaining(long started, RabbitMqOptions settings) => TimeSpan.FromTicks(Math.Max(0,
        (TimeSpan.FromSeconds(settings.PublishBudgetSeconds) - Stopwatch.GetElapsedTime(started)).Ticks));
    private async Task EnsureChannelAsync(RabbitMqOptions settings, long started, CancellationToken token)
    {
        if (channel is null || !channel.IsOpen || !connection!.IsOpen)
        {
            await ResetAsync(Remaining(started, settings));
            connection = await factory.CreateConnectionAsync(token).WaitAsync(token);
            channel = await connection.CreateChannelAsync(new CreateChannelOptions(true, true), token).WaitAsync(token);
            await channel.ExchangeDeclareAsync(settings.Exchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: token).WaitAsync(token);
            await channel.QueueDeclareAsync(settings.Queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: token).WaitAsync(token);
            await channel.QueueBindAsync(settings.Queue, settings.Exchange, settings.RoutingKey, cancellationToken: token).WaitAsync(token);
        }
    }
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var started = Stopwatch.GetTimestamp();
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds(settings.PublishBudgetSeconds));
        await gate.WaitAsync(budget.Token);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            await EnsureChannelAsync(settings, started, budget.Token);
            // Passive declarations verify existing topology capability; never publish a dummy job.
            await channel!.ExchangeDeclarePassiveAsync(settings.Exchange, budget.Token).WaitAsync(budget.Token);
            await channel.QueueDeclarePassiveAsync(settings.Queue, budget.Token).WaitAsync(budget.Token);
        }
        catch (Exception exception)
        {
            await ResetAsync(Remaining(started, settings));
            if (exception is OperationCanceledException && cancellationToken.IsCancellationRequested) throw;
            throw new JobPublicationException(PublicationFailure.NotAccepted, exception);
        }
        finally { gate.Release(); }
    }
    private async Task ResetAsync(TimeSpan? limit = null)
    {
        var started = Stopwatch.GetTimestamp();
        var maximum = limit ?? TimeSpan.FromSeconds(2);
        TimeSpan Left() => TimeSpan.FromTicks(Math.Clamp((maximum - Stopwatch.GetElapsedTime(started)).Ticks, 0, TimeSpan.FromSeconds(1).Ticks));
        var oldChannel = channel; channel = null;
        var oldConnection = connection; connection = null;
        try { if (oldChannel is not null) await oldChannel.DisposeAsync().AsTask().WaitAsync(Left()); }
        catch (Exception exception) { logger.LogWarning("RabbitMQ channel disposal failed; failure {Failure}", exception.GetType().Name); }
        try { if (oldConnection is not null) await oldConnection.DisposeAsync().AsTask().WaitAsync(Left()); }
        catch (Exception exception) { logger.LogWarning("RabbitMQ connection disposal failed; failure {Failure}", exception.GetType().Name); }
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { disposed = true; await ResetAsync(); }
        finally { gate.Release(); }
    }
}
