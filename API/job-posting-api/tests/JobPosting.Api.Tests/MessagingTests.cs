using System.Reflection;
using System.Text.Json;
using JobPosting.Api.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace JobPosting.Api.Tests;

[Trait("Category", "Unit")]
public sealed class MessagingTests
{
    private static JobPostingCreated Event() => JobPostingCreated.FromSaved(PersistenceTests.Posting().Job, "trace");
    [Fact]
    public void EnvelopeContainsOnlySavedFieldsAndStableEventMetadata()
    {
        var row = PersistenceTests.Posting().Job;
        var envelope = JobPostingCreated.FromSaved(row, "trace");
        Assert.Equal(row.EventId, envelope.EventId);
        Assert.Equal(row.CreatedAt, envelope.OccurredAt);
        Assert.Equal("JobPostingCreated", envelope.EventType);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal("trace", envelope.CorrelationId);
        Assert.Equal(row.Id.ToString("D"), envelope.Job.Id);
        Assert.Equal(row.CreatedAt, envelope.Job.CreatedAt);
        Assert.Equal(row.Title, envelope.Job.Title);
        Assert.Equal(row.Department, envelope.Job.Department);
        Assert.Equal(row.Location, envelope.Job.Location);
        Assert.Equal(row.Description, envelope.Job.Description);
        Assert.Equal(row.SalaryMin, envelope.Job.SalaryMin);
        Assert.Equal(row.SalaryMax, envelope.Job.SalaryMax);
        Assert.Equal(row.ClosingDate, envelope.Job.ClosingDate);
        var json = JsonSerializer.Serialize(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("fingerprint", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("keyDigest", json);
    }
    [Fact]
    public async Task ConfirmedPublicationIsPersistentMandatoryAndReusesChannelAndConnection()
    {
        var wire = new Wire();
        await using var publisher = wire.Publisher();
        var envelope = Event();
        await publisher.PublishAsync(envelope, default);
        await publisher.PublishAsync(envelope, default);
        Assert.Equal(1, wire.Connections);
        Assert.Equal(1, wire.Channels);
        Assert.Equal(2, wire.Publications);
        Assert.True(wire.ChannelOptions!.PublisherConfirmationsEnabled);
        Assert.True(wire.ChannelOptions.PublisherConfirmationTrackingEnabled);
        Assert.Equal("job-post-exchange", wire.Exchange);
        Assert.Equal("direct", wire.ExchangeType);
        Assert.True(wire.DurableExchange);
        Assert.True(wire.DurableQueue);
        Assert.Equal("job-post-queue", wire.Queue);
        Assert.Equal("job-posting.created.v1", wire.Binding);
        Assert.True(wire.Mandatory);
        Assert.True(wire.Properties!.Persistent);
        Assert.Equal("application/json", wire.Properties.ContentType);
        Assert.Equal("utf-8", wire.Properties.ContentEncoding);
        Assert.Equal(envelope.EventId.ToString("D"), wire.Properties.MessageId);
        Assert.Equal(envelope.CorrelationId, wire.Properties.CorrelationId);
        Assert.Equal(envelope.EventType, wire.Properties.Type);
        Assert.Equal(envelope.OccurredAt.ToUnixTimeSeconds(), wire.Properties.Timestamp.UnixTime);
        Assert.Equal(1, wire.Properties.Headers!["schemaVersion"]);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(envelope, new JsonSerializerOptions(JsonSerializerDefaults.Web)), wire.Body);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ClosedChannelOrConnectionIsRepairedWithoutReplayingMessages(bool connectionClosed)
    {
        var wire = new Wire(); await using var publisher = wire.Publisher();
        await publisher.PublishAsync(Event(), default);
        wire.ChannelOpen = connectionClosed;
        wire.ConnectionOpen = !connectionClosed;
        await publisher.PublishAsync(Event(), default);
        Assert.Equal(2, wire.Connections); Assert.Equal(2, wire.Publications);
        Assert.Equal(1, wire.ConnectionDisposals); Assert.Equal(1, wire.ChannelDisposals);
    }
    [Theory]
    [InlineData("nack", PublicationFailure.NotAccepted)]
    [InlineData("return", PublicationFailure.NotAccepted)]
    [InlineData("lost", PublicationFailure.AcceptanceUnknown)]
    [InlineData("connect", PublicationFailure.NotAccepted)]
    [InlineData("channel", PublicationFailure.NotAccepted)]
    [InlineData("topology", PublicationFailure.NotAccepted)]
    public async Task FailureNeverReportsConfirmationOrAutomaticallyRetries(string scenario, PublicationFailure expected)
    {
        var wire = new Wire();
        if (scenario is "connect" or "channel" or "topology") wire.SetupFailure = scenario;
        else wire.Publish = _ => ValueTask.FromException(scenario is "nack" or "return" ? new PublishException(1, scenario == "return") : new IOException("lost ack"));
        await using var publisher = wire.Publisher();
        var error = await Assert.ThrowsAsync<JobPublicationException>(() => publisher.PublishAsync(Event(), default));
        Assert.Equal(expected, error.Failure);
        Assert.Contains("could not be confirmed", error.Message);
        Assert.Equal(scenario is "connect" or "channel" or "topology" ? 0 : 1, wire.Publications);
        wire.SetupFailure = ""; wire.Publish = _ => ValueTask.CompletedTask;
        await publisher.PublishAsync(Event(), default);
        Assert.Equal(2, wire.Connections);
    }
    [Fact]
    public async Task ConfirmTimeoutIsUnknownAndCallerCancellationPropagates()
    {
        var wire = new Wire { Publish = token => new ValueTask(Task.Delay(Timeout.Infinite, token)) };
        await using var publisher = wire.Publisher(new() { ConfirmTimeoutSeconds = 1, PublishBudgetSeconds = 2 });
        var error = await Assert.ThrowsAsync<JobPublicationException>(() => publisher.PublishAsync(Event(), default));
        Assert.Equal(PublicationFailure.AcceptanceUnknown, error.Failure);
        using var cancellation = new CancellationTokenSource();
        wire.Publish = _ => { cancellation.Cancel(); return ValueTask.FromException(new OperationCanceledException(cancellation.Token)); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(Event(), cancellation.Token));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(Event(), cancellation.Token));
    }
    [Fact]
    public async Task QueuedCancellationDoesNotDisposeAnotherPublishersConnection()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var wire = new Wire { Publish = _ => { entered.SetResult(); return new(finish.Task); } };
        await using var publisher = wire.Publisher(new() { PublishBudgetSeconds = 3, ConfirmTimeoutSeconds = 3 });
        var first = publisher.PublishAsync(Event(), default);
        await entered.Task;
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.PublishAsync(Event(), cancellation.Token));
        Assert.Equal(0, wire.ConnectionDisposals);
        finish.SetResult(); await first;
    }
    [Fact]
    public async Task DisposalFailuresCannotMaskPublishFailureAndDisposedPublisherCannotSend()
    {
        var wire = new Wire { Publish = _ => ValueTask.FromException(new IOException()), DisposalFails = true };
        var publisher = wire.Publisher();
        Assert.Equal(PublicationFailure.AcceptanceUnknown, (await Assert.ThrowsAsync<JobPublicationException>(() => publisher.PublishAsync(Event(), default))).Failure);
        await publisher.DisposeAsync(); await publisher.DisposeAsync();
        var error = await Assert.ThrowsAsync<JobPublicationException>(() => publisher.PublishAsync(Event(), default));
        Assert.IsType<ObjectDisposedException>(error.InnerException);
    }
    [Fact]
    public async Task ConfigurationIsSafeAndFactoryRegistrationNeedsNoBroker()
    {
        var invalid = new RabbitMqOptions { HostName = " ", Port = 0, UserName = "", Password = "", VirtualHost = "", Exchange = "", Queue = new string('x', 256), RoutingKey = "amq.reserved", PublishBudgetSeconds = 0, ConfirmTimeoutSeconds = 0 };
        var validation = new RabbitMqOptionsValidator();
        Assert.True(validation.Validate(null, invalid).Failed);
        Assert.True(validation.Validate(null, new() { Port = 65536, PublishBudgetSeconds = 11, ConfirmTimeoutSeconds = 4 }).Failed);
        Assert.True(validation.Validate(null, new() { PublishBudgetSeconds = 1, ConfirmTimeoutSeconds = 2 }).Failed);
        Assert.True(validation.Validate(null, new() { Port = 1, PublishBudgetSeconds = 1, ConfirmTimeoutSeconds = 1 }).Succeeded);
        Assert.True(validation.Validate(null, new() { Port = 65535, PublishBudgetSeconds = 10, ConfirmTimeoutSeconds = 3 }).Succeeded);
        Assert.DoesNotContain("guest", validation.Validate(null, invalid).FailureMessage);
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddJobMessaging(new ConfigurationBuilder().Build());
        await using var provider = services.BuildServiceProvider();
        var factory = Assert.IsType<ConnectionFactory>(provider.GetRequiredService<IConnectionFactory>());
        Assert.Equal("localhost", factory.HostName); Assert.Equal(5672, factory.Port);
        Assert.Equal("guest", factory.UserName); Assert.Equal("guest", factory.Password); Assert.Equal("/", factory.VirtualHost);
        Assert.False(factory.AutomaticRecoveryEnabled); Assert.False(factory.TopologyRecoveryEnabled);
        Assert.Equal("job-posting-api", factory.ClientProvidedName);
        Assert.Equal(TimeSpan.FromSeconds(3), factory.RequestedConnectionTimeout);
        Assert.IsType<JobPosting.Api.Resilience.PublicationCircuit>(provider.GetRequiredService<IJobEventPublisher>());
        Assert.Same(provider.GetRequiredService<IJobEventPublisher>(), provider.GetRequiredService<JobPosting.Api.Resilience.IPublisherProbe>());
    }
    [Theory]
    [InlineData("success")]
    [InlineData("failure")]
    [InlineData("cancel")]
    [InlineData("disposed")]
    [InlineData("queued")]
    public async Task ReadinessProbeNeverPublishesAndRepairsOrFailsSafely(string mode)
    {
        var wire = new Wire(); await using var publisher = wire.Publisher();
        using var cancellation = new CancellationTokenSource();
        if (mode == "failure") wire.SetupFailure = "connect";
        if (mode == "cancel") wire.Passive = () => { cancellation.Cancel(); return Task.FromCanceled(cancellation.Token); };
        if (mode == "disposed") await publisher.DisposeAsync();
        if (mode == "queued") cancellation.Cancel();
        if (mode == "success") { await publisher.ProbeAsync(default); await publisher.ProbeAsync(default); Assert.Equal(1, wire.Connections); }
        else if (mode is "cancel" or "queued") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => publisher.ProbeAsync(cancellation.Token));
        else await Assert.ThrowsAsync<JobPublicationException>(() => publisher.ProbeAsync(default));
        Assert.Equal(0, wire.Publications);
    }
    [Fact]
    public async Task OverallBudgetAlsoBoundsUnresponsiveConfirmationAndResourceReset()
    {
        var wire = new Wire { Publish = _ => new ValueTask(Task.Delay(Timeout.Infinite)) }; await using var publisher = wire.Publisher(new() { PublishBudgetSeconds = 1, ConfirmTimeoutSeconds = 1 });
        var watch = System.Diagnostics.Stopwatch.StartNew(); var failure = await Assert.ThrowsAsync<JobPublicationException>(() => publisher.PublishAsync(Event(), default)); Assert.Equal(PublicationFailure.AcceptanceUnknown, failure.Failure);
        Assert.InRange(watch.Elapsed.TotalSeconds, 0.8, 1.8); Assert.Equal(1, wire.Publications);
    }
    public class ApiProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Handler = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args!);
        public static T For<T>(Func<MethodInfo, object?[], object?> handler) where T : class
        { var proxy = Create<T, ApiProxy>(); ((ApiProxy)(object)proxy).Handler = handler; return proxy; }
    }
    private sealed class Wire
    {
        public int Connections, Channels, Publications, ChannelDisposals, ConnectionDisposals;
        public bool ChannelOpen = true, ConnectionOpen = true, DisposalFails;
        public string SetupFailure = "";
        public Func<Task> Passive = () => Task.CompletedTask;
        public Func<CancellationToken, ValueTask> Publish = _ => ValueTask.CompletedTask;
        public CreateChannelOptions? ChannelOptions;
        public string? Exchange, ExchangeType, Queue, Binding;
        public bool DurableExchange, DurableQueue, Mandatory;
        public BasicProperties? Properties;
        public byte[]? Body;
        public RabbitMqJobEventPublisher Publisher(RabbitMqOptions? settings = null)
        {
            var channel = ApiProxy.For<IChannel>((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_IsOpen": return ChannelOpen;
                    case "ExchangeDeclarePassiveAsync": return Passive();
                    case "QueueDeclarePassiveAsync": return Task.FromResult(new QueueDeclareOk("queue", 0, 0));
                    case "ExchangeDeclareAsync":
                        Exchange = (string)args[0]!; ExchangeType = (string)args[1]!; DurableExchange = (bool)args[2]!;
                        return SetupFailure == "topology" ? Task.FromException(new IOException()) : Task.CompletedTask;
                    case "QueueDeclareAsync": Queue = (string)args[0]!; DurableQueue = (bool)args[1]!; return Task.FromResult(new QueueDeclareOk(Queue, 0, 0));
                    case "QueueBindAsync": Binding = (string)args[2]!; return Task.CompletedTask;
                    case "BasicPublishAsync": Publications++; Mandatory = (bool)args[2]!; Properties = (BasicProperties)args[3]!; Body = ((ReadOnlyMemory<byte>)args[4]!).ToArray(); return Publish((CancellationToken)args[5]!);
                    case "DisposeAsync": ChannelDisposals++; return DisposalFails ? ValueTask.FromException(new IOException()) : ValueTask.CompletedTask;
                    default: throw new NotSupportedException(method.Name);
                }
            });
            var connection = ApiProxy.For<IConnection>((method, args) =>
            {
                switch (method.Name)
                {
                    case "get_IsOpen": return ConnectionOpen;
                    case "CreateChannelAsync": Channels++; ChannelOptions = (CreateChannelOptions)args[0]!; return SetupFailure == "channel" ? Task.FromException<IChannel>(new IOException()) : Task.FromResult(channel);
                    case "DisposeAsync": ConnectionDisposals++; return DisposalFails ? ValueTask.FromException(new IOException()) : ValueTask.CompletedTask;
                    default: throw new NotSupportedException(method.Name);
                }
            });
            var factory = ApiProxy.For<IConnectionFactory>((method, args) =>
            {
                Connections++; ChannelOpen = true; ConnectionOpen = true;
                return SetupFailure == "connect" ? Task.FromException<IConnection>(new IOException()) : Task.FromResult(connection);
            });
            return new(factory, Options.Create(settings ?? new()), NullLogger<RabbitMqJobEventPublisher>.Instance);
        }
    }
}
