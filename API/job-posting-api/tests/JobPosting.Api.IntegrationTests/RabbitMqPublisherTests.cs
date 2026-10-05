using System.Text.Json;
using JobPosting.Api.Contracts;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;
using JobPosting.Api.Validation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace JobPosting.Api.IntegrationTests;

[Trait("Category", "Integration")]
public sealed class RabbitMqPublisherTests(RabbitMqFixture broker) : IClassFixture<RabbitMqFixture>
{
    private static JobPostingCreated Event()
    {
        var payload = new JobRequestValidator().Normalize(new CreateJobRequest { Title="Engineer", Department="Engineering", Location="Toronto", Description="Plain text", SalaryMin=10m, SalaryMax=100m, ClosingDate=new(2028,2,29) }).Request!;
        var row = PendingPosting.Create(payload,new string('a',64),Guid.NewGuid(),Guid.NewGuid(),DateTimeOffset.UtcNow,"trace").Job;
        return JobPostingCreated.FromSaved(row,"trace");
    }
    private static RabbitMqJobEventPublisher Publisher(RabbitMqOptions settings) => new(RabbitMqMessaging.CreateFactory(settings),Options.Create(settings),NullLogger<RabbitMqJobEventPublisher>.Instance);
    [Fact]
    public async Task ReadinessCanRepairTopologyWithoutPublishingOrReplayingJobs()
    {
        var settings=broker.Settings();await using var publisher=Publisher(settings);
        await publisher.ProbeAsync(default);
        await using var connection=await RabbitMqMessaging.CreateFactory(settings).CreateConnectionAsync();await using var channel=await connection.CreateChannelAsync();
        Assert.Null(await channel.BasicGetAsync(settings.Queue,true));
        await channel.QueueDeleteAsync(settings.Queue);
        await Assert.ThrowsAsync<JobPublicationException>(()=>publisher.ProbeAsync(default));
        await publisher.ProbeAsync(default);Assert.Null(await channel.BasicGetAsync(settings.Queue,true));
    }
    [Fact]
    public async Task ConcurrentConfirmedMessagesHavePersistentPropertiesAndCompleteImmutableEnvelope()
    {
        var settings = broker.Settings(); await using var publisher = Publisher(settings);
        var events = Enumerable.Range(0,8).Select(_=>Event()).ToArray();
        await Task.WhenAll(events.Select(envelope=>publisher.PublishAsync(envelope,default)));
        await using var connection = await RabbitMqMessaging.CreateFactory(settings).CreateConnectionAsync();
        await using var channel = await connection.CreateChannelAsync();
        var declared = await channel.QueueDeclarePassiveAsync(settings.Queue);
        Assert.Equal(8u, declared.MessageCount);
        var identities = new HashSet<Guid>();
        for (var index=0;index<8;index++)
        {
            var message = await channel.BasicGetAsync(settings.Queue,autoAck:true);
            Assert.NotNull(message); Assert.Equal(settings.Exchange,message.Exchange); Assert.Equal(settings.RoutingKey,message.RoutingKey);
            Assert.Equal(DeliveryModes.Persistent,message.BasicProperties.DeliveryMode); Assert.Equal("application/json",message.BasicProperties.ContentType);
            Assert.Equal("utf-8",message.BasicProperties.ContentEncoding); Assert.Equal("trace",message.BasicProperties.CorrelationId);
            Assert.Equal("JobPostingCreated",message.BasicProperties.Type); Assert.Equal(1,message.BasicProperties.Headers!["schemaVersion"]);
            using var body = JsonDocument.Parse(message.Body); var id=body.RootElement.GetProperty("eventId").GetGuid();
            Assert.Equal(id.ToString("D"),message.BasicProperties.MessageId); Assert.True(identities.Add(id));
            var original=events.Single(envelope=>envelope.EventId==id);
            Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(original,new JsonSerializerOptions(JsonSerializerDefaults.Web)),message.Body.ToArray());
            Assert.Equal(original.OccurredAt.ToUnixTimeSeconds(),message.BasicProperties.Timestamp.UnixTime);
        }
        Assert.Null(await channel.BasicGetAsync(settings.Queue,true));
    }
    [Fact]
    public async Task MandatoryUnroutableMessageFailsAndNextInvocationRepairsCapabilityWithoutReplay()
    {
        var settings=broker.Settings(); await using var publisher=Publisher(settings);
        await publisher.PublishAsync(Event(),default);
        await using var connection=await RabbitMqMessaging.CreateFactory(settings).CreateConnectionAsync();
        await using var channel=await connection.CreateChannelAsync();
        await channel.QueueDeleteAsync(settings.Queue);
        var error=await Assert.ThrowsAsync<JobPublicationException>(()=>publisher.PublishAsync(Event(),default));
        Assert.Equal(PublicationFailure.NotAccepted,error.Failure);
        var next=Event(); await publisher.PublishAsync(next,default);
        var result=await channel.BasicGetAsync(settings.Queue,true); Assert.NotNull(result);
        Assert.Equal(next.EventId.ToString("D"),result.BasicProperties.MessageId);
        Assert.Null(await channel.BasicGetAsync(settings.Queue,true));
    }
    [Fact]
    public async Task DurablePersistentMessageSurvivesBrokerRestartAndPublisherReconnectsForNewMessage()
    {
        var settings=broker.Settings(); await using var publisher=Publisher(settings);
        var before=Event(); await publisher.PublishAsync(before,default);
        await broker.RestartAsync();
        await using var connection=await RabbitMqMessaging.CreateFactory(settings).CreateConnectionAsync();
        await using var channel=await connection.CreateChannelAsync();
        var retained=await channel.BasicGetAsync(settings.Queue,true); Assert.NotNull(retained);
        Assert.Equal(before.EventId.ToString("D"),retained.BasicProperties.MessageId);
        var after=Event(); await publisher.PublishAsync(after,default);
        var current=await channel.BasicGetAsync(settings.Queue,true); Assert.NotNull(current);
        Assert.Equal(after.EventId.ToString("D"),current.BasicProperties.MessageId);
        Assert.Null(await channel.BasicGetAsync(settings.Queue,true));
    }
}
