using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using JobSearch.Api.Diagnostics;
namespace JobSearch.Api.Messaging;
public sealed record QuarantineMessage(ReadOnlyMemory<byte> Body,string Code,string? MessageId,string? CorrelationId,string? ContentType);
public interface IQuarantinePublisher { Task PublishAsync(QuarantineMessage message,CancellationToken token); }
public sealed class QuarantineException(Exception failure):Exception("Quarantine acceptance is unresolved; do not acknowledge the original delivery.",failure);
public sealed class RabbitMqQuarantinePublisher(IConnectionFactory factory,IOptions<ConsumerOptions> options,TimeProvider clock,ILogger<RabbitMqQuarantinePublisher> logger):IQuarantinePublisher
{
    public async Task PublishAsync(QuarantineMessage message,CancellationToken token)
    {
        var o=options.Value;
        using var budget=new CancellationTokenSource(TimeSpan.FromSeconds(o.QuarantineBudgetSeconds),clock);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,budget.Token);
        IConnection? connection=null;IChannel? channel=null;
        try
        {
            connection=await factory.CreateConnectionAsync(linked.Token).WaitAsync(linked.Token);
            channel=await connection.CreateChannelAsync(new CreateChannelOptions(true,true),linked.Token).WaitAsync(linked.Token);
            await channel.ExchangeDeclareAsync(o.QuarantineExchange,ExchangeType.Direct,true,false,cancellationToken:linked.Token).WaitAsync(linked.Token);
            await channel.QueueDeclareAsync(o.QuarantineQueue,true,false,false,cancellationToken:linked.Token).WaitAsync(linked.Token);
            await channel.QueueBindAsync(o.QuarantineQueue,o.QuarantineExchange,o.QuarantineRoutingKey,cancellationToken:linked.Token).WaitAsync(linked.Token);
            var properties=new BasicProperties { Persistent=true,ContentType="application/octet-stream",Type="JobSearchQuarantined",MessageId=message.MessageId,CorrelationId=message.CorrelationId,
                Timestamp=new AmqpTimestamp(clock.GetUtcNow().ToUnixTimeSeconds()),Headers=new Dictionary<string,object?> { ["failureCode"]=message.Code,["sourceContentType"]=message.ContentType } };
            // Own the buffer if a timed-out dependency retains it beyond the consumer callback.
            await channel.BasicPublishAsync(o.QuarantineExchange,o.QuarantineRoutingKey,true,properties,message.Body.ToArray(),linked.Token).AsTask().WaitAsync(linked.Token);
            SearchMetrics.Quarantine("confirmed");
        }
        catch(Exception ex)
        {
            SearchMetrics.Quarantine("failed");
            throw new QuarantineException(ex);
        }
        finally
        {
            if(channel is not null)await CleanupAsync(channel,linked.Token);
            if(connection is not null)await CleanupAsync(connection,linked.Token);
        }
    }
    private async Task CleanupAsync(IAsyncDisposable resource,CancellationToken token)
    {
        try { await resource.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(1),clock,token); }
        catch(Exception ex) { logger.LogWarning(new EventId(4202,"QuarantineDisposeFailed"),"Quarantine resource cleanup failed; failure {Failure}",ex.GetType().Name); }
    }
}
