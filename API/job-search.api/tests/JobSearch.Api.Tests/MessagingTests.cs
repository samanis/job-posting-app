using System.Reflection;
using System.Text;
using JobSearch.Api.Contracts;
using JobSearch.Api.Configuration;
using JobSearch.Api.Messaging;
using JobSearch.Api.Persistence;
using JobSearch.Api.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
namespace JobSearch.Api.Tests;

[Trait("Category", "Unit")]
public sealed class MessagingTests
{
    private static byte[] Body => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures/job-created-v1.json"));
    [Theory]
    [InlineData(ProjectionOutcome.Inserted)]
    [InlineData(ProjectionOutcome.Duplicate)]
    [InlineData(ProjectionOutcome.Conflict)]
    public async Task HandlerOnlyAuthorizesCommittedOrVerifiedDuplicate(ProjectionOutcome outcome)
    {
        var projection = new Projection { Outcome = outcome }; var handler = new SearchEventHandler(new(), Options.Create(new SearchOptions()), projection);
        if (outcome == ProjectionOutcome.Conflict) Assert.Equal("identity_conflict", (await Assert.ThrowsAsync<PermanentDeliveryException>(() => handler.HandleAsync(Body, new(), default))).Code);
        else Assert.Equal(outcome == ProjectionOutcome.Inserted ? DeliveryOutcome.Inserted : DeliveryOutcome.Duplicate, await handler.HandleAsync(Body, new(), default));
        Assert.Equal(1, projection.Calls);
    }
    [Fact]
    public async Task InvalidUncertainAndCanceledWorkCannotAuthorizeAck()
    {
        var projection = new Projection(); var handler = new SearchEventHandler(new(), Options.Create(new SearchOptions()), projection);
        Assert.Equal("invalid_event", (await Assert.ThrowsAsync<PermanentDeliveryException>(() => handler.HandleAsync(Encoding.UTF8.GetBytes("{}"), new(), default))).Code); Assert.Equal(0, projection.Calls);
        projection.Failure = new ProjectionCommitUncertainException(new IOException("private")); await Assert.ThrowsAsync<ProjectionCommitUncertainException>(() => handler.HandleAsync(Body, new(), default));
        using var canceled = new CancellationTokenSource(); canceled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.HandleAsync(Body, new(), canceled.Token));
    }
    [Fact]
    public async Task ConfigRegistrationIsIndependentAndBounded()
    {
        var v = new ConsumerOptionsValidator(); var defaults = new ConsumerOptions(); Assert.True(v.Validate(null, defaults).Succeeded);
        var invalid = new ConsumerOptions { HostName = " ", Port = 0, UserName = "", Password = "", VirtualHost = "", Exchange = "", Queue = new string('x', 256), RoutingKey = "amq.invalid", Prefetch = 0, Concurrency = 0 }; Assert.True(v.Validate(null, invalid).Failed); Assert.DoesNotContain("guest", v.Validate(null, invalid).FailureMessage);
        Assert.True(v.Validate(null, new() { Port = 65536, Prefetch = 101, Concurrency = 11 }).Failed); Assert.True(v.Validate(null, new() { Prefetch = 1, Concurrency = 2 }).Failed);
        Assert.True(v.Validate(null, new() { Port = 1, Prefetch = 1, Concurrency = 1 }).Succeeded); Assert.True(v.Validate(null, new() { Port = 65535, Prefetch = 100, Concurrency = 10 }).Succeeded);
        var services = new ServiceCollection().AddLogging(); services.AddSingleton<TimeProvider>(TimeProvider.System); services.AddSingleton<ISearchProjection>(new Projection()); services.AddOptions<SearchOptions>(); services.AddSearchMessaging(new ConfigurationBuilder().Build()); await using var provider = services.BuildServiceProvider();
        var factory = Assert.IsType<ConnectionFactory>(provider.GetRequiredService<IConnectionFactory>()); Assert.Equal("localhost", factory.HostName); Assert.Equal(5672, factory.Port); Assert.Equal("guest", factory.UserName); Assert.Equal("guest", factory.Password); Assert.Equal("/", factory.VirtualHost); Assert.Equal("job-search-api", factory.ClientProvidedName); Assert.False(factory.AutomaticRecoveryEnabled); Assert.False(factory.TopologyRecoveryEnabled); Assert.Equal(TimeSpan.FromSeconds(3), factory.RequestedConnectionTimeout);
        Assert.NotNull(provider.GetRequiredService<IConsumerSession>()); await using var scope = provider.CreateAsyncScope(); Assert.NotNull(scope.ServiceProvider.GetRequiredService<ISearchEventHandler>()); Assert.Single(provider.GetServices<IHostedService>());
    }
    [Fact]
    public void MetadataAcceptsOnlyCompatibleIntegerHeaders()
    {
        Assert.Null(RabbitMqConsumerSession.Metadata(new BasicProperties()).SchemaVersion);
        Assert.Null(RabbitMqConsumerSession.Metadata(new BasicProperties { Headers = new Dictionary<string, object?>() }).SchemaVersion);
        foreach (var value in new object[] { (byte)1, (sbyte)1, (short)1, (ushort)1, 1, 1L }) Assert.Equal(1, RabbitMqConsumerSession.Metadata(new BasicProperties { Headers = new Dictionary<string, object?> { { "schemaVersion", value } }, MessageId = "id", Type = "type", ContentType = "application/json" }).SchemaVersion);
        foreach (var value in new object[] { "1", 1.0, long.MaxValue }) Assert.Equal("invalid_metadata", Assert.Throws<PermanentDeliveryException>(() => RabbitMqConsumerSession.Metadata(new BasicProperties { Headers = new Dictionary<string, object?> { { "schemaVersion", value } } })).Code);
    }
    [Theory]
    [InlineData("success")]
    [InlineData("handler")]
    [InlineData("closed")]
    [InlineData("ack")]
    [InlineData("cancel")]
    [InlineData("metadata")]
    [InlineData("shutdown")]
    [InlineData("unregistered")]
    public async Task SessionSettlesOnlyOnOriginalOpenChannelAfterSuccessfulHandling(string mode)
    {
        var wire = new Wire(); using var cancel = new CancellationTokenSource(); var handler = new Handler();
        await using var services = new ServiceCollection().AddSingleton<ISearchEventHandler>(handler).BuildServiceProvider();
        var session = wire.Session(services); var run = session.RunAsync(cancel.Token); await wire.Subscribed.Task;
        Assert.False(wire.AutoAck); Assert.Equal((ushort)10, wire.Prefetch); Assert.Equal((ushort)1, wire.ChannelOptions!.ConsumerDispatchConcurrency); Assert.True(wire.DurableQueue); Assert.True(wire.DurableExchange); Assert.Equal("job-posting.created.v1", wire.Binding);
        if (mode == "handler") handler.Work = (_) => Task.FromException<DeliveryOutcome>(new ProjectionCommitUncertainException(new IOException()));
        if (mode == "closed") wire.Open = false;
        if (mode == "ack") wire.AckFailure = new IOException();
        if (mode == "cancel") handler.Work = async t => { cancel.Cancel(); await Task.Delay(Timeout.Infinite, t); return DeliveryOutcome.Inserted; };
        if (mode == "shutdown") { wire.Open = false; await wire.Shutdown!(wire.Channel, new ShutdownEventArgs(ShutdownInitiator.Peer, 320, "safe")); }
        else if (mode == "unregistered") await wire.Consumer!.HandleBasicCancelOkAsync("tag", default);
        else await wire.DeliverAsync(mode == "metadata" ? new BasicProperties { Headers = new Dictionary<string, object?> { { "schemaVersion", "1" } } } : new BasicProperties());
        if (mode == "success") { Assert.Equal(new[] { "project", "ack" }, handler.Calls.Concat(wire.Calls)); Assert.Equal(7UL, wire.AckTag); Assert.False(wire.Multiple); cancel.Cancel(); }
        await Assert.ThrowsAnyAsync<Exception>(() => run.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(mode is "success" or "ack" ? 1 : 0, wire.Acks); Assert.Equal(1, wire.ConnectionDisposals); Assert.Equal(1, wire.ChannelDisposals);
    }
    [Theory]
    [InlineData("connect")]
    [InlineData("channel")]
    [InlineData("topology")]
    [InlineData("dispose")]
    public async Task SetupFailureDisposesOnlyOwnedResourcesAndLogsNoDetails(string mode)
    {
        var wire = new Wire { Failure = mode }; await using var services = new ServiceCollection().AddSingleton<ISearchEventHandler>(new Handler()).BuildServiceProvider(); using var cancel = new CancellationTokenSource(); using var logs = new CapturedLoggerProvider(); using var lf = LoggerFactory.Create(b => b.AddProvider(logs)); var run = wire.Session(services, lf.CreateLogger<RabbitMqConsumerSession>()).RunAsync(cancel.Token);
        if (mode == "dispose") { await wire.Subscribed.Task; cancel.Cancel(); }
        await Assert.ThrowsAnyAsync<Exception>(() => run); Assert.Equal(mode == "connect" ? 0 : 1, wire.ConnectionDisposals); Assert.Equal(mode is "connect" or "channel" ? 0 : 1, wire.ChannelDisposals);
        if (mode == "dispose") { Assert.Equal(2, logs.Entries.Count); Assert.All(logs.Entries, e => Assert.Null(e.Exception)); }
    }
    [Fact]
    public async Task BackpressureLimitsProcessingAndCancellationLeavesQueuedDeliveryUnacked()
    {
        var wire = new Wire(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var handler = new Handler { Work = async t => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, t); return DeliveryOutcome.Inserted; } };
        await using var services = new ServiceCollection().AddSingleton<ISearchEventHandler>(handler).BuildServiceProvider(); using var cancel = new CancellationTokenSource(); var run = wire.Session(services).RunAsync(cancel.Token); await wire.Subscribed.Task;
        var first = wire.DeliverAsync(new()); await entered.Task; var second = wire.DeliverAsync(new()); Assert.Single(handler.Calls); cancel.Cancel(); await Task.WhenAll(first, second); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run); Assert.Equal(0, wire.Acks);
    }
    [Fact]
    public async Task LateCompletionCannotAckOnClosedOrReplacementSession()
    {
        var oldWire = new Wire(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldHandler = new Handler { Work = async _ => { entered.TrySetResult(); await finish.Task; return DeliveryOutcome.Inserted; } };
        await using var oldServices = new ServiceCollection().AddSingleton<ISearchEventHandler>(oldHandler).BuildServiceProvider(); using var stopOld = new CancellationTokenSource(); var oldRun = oldWire.Session(oldServices).RunAsync(stopOld.Token); await oldWire.Subscribed.Task; var late = oldWire.DeliverAsync(new()); await entered.Task; stopOld.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => oldRun);
        var newWire = new Wire(); await using var newServices = new ServiceCollection().AddSingleton<ISearchEventHandler>(new Handler()).BuildServiceProvider(); using var stopNew = new CancellationTokenSource(); var newRun = newWire.Session(newServices).RunAsync(stopNew.Token); await newWire.Subscribed.Task;
        finish.TrySetResult(); await late; Assert.Equal(0, oldWire.Acks); Assert.Equal(0, newWire.Acks);
        await newWire.DeliverAsync(new()); Assert.Equal(1, newWire.Acks); stopNew.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => newRun);
    }
    [Theory]
    [InlineData("disabled")]
    [InlineData("precancel")]
    [InlineData("cancel-session")]
    [InlineData("transient")]
    [InlineData("permanent")]
    [InlineData("normal")]
    public async Task WorkerCanStopAndDoesNotPoisonLoop(string mode)
    {
        using var cancel = new CancellationTokenSource(); var calls = 0; var second = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var session = new Session(t => { calls++; if (calls == 2) second.TrySetResult(); if (mode == "cancel-session") { cancel.Cancel(); return Task.FromCanceled(t); } if (mode == "permanent") return Task.FromException(new PermanentDeliveryException("invalid_event")); if (mode == "transient") return Task.FromException(new IOException("private")); return Task.CompletedTask; });
        using var logs = new CapturedLoggerProvider(); using var lf = LoggerFactory.Create(b => b.AddProvider(logs)); using var worker = new SearchConsumerWorker(session, Options.Create(new ConsumerOptions { Enabled = mode != "disabled" }), TimeProvider.System, lf.CreateLogger<SearchConsumerWorker>(), new IngestionState(), new BackoffJitter());
        if (mode == "precancel") cancel.Cancel();
        var run = worker.RunAsync(cancel.Token);
        if (mode == "normal") { await second.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel(); }
        if (mode is "transient" or "permanent") { await Task.Delay(30); cancel.Cancel(); }
        await run; Assert.Equal(mode is "disabled" or "precancel" ? 0 : mode == "normal" ? 2 : 1, calls); Assert.All(logs.Entries, e => { Assert.Null(e.Exception); Assert.DoesNotContain("private", string.Join(',', e.Properties.Values)); });
        if (mode == "disabled") { await worker.StartAsync(default); await worker.StopAsync(default); }
    }
    private sealed class Projection : ISearchProjection
    { public int Calls; public ProjectionOutcome Outcome; public Exception? Failure; public Task<ProjectionOutcome> ProjectAsync(JobCreatedEvent e, CancellationToken t) { Calls++; return Failure is null ? Task.FromResult(Outcome) : Task.FromException<ProjectionOutcome>(Failure); } }
    private sealed class Handler : ISearchEventHandler
    { public List<string> Calls = new(); public Func<CancellationToken, Task<DeliveryOutcome>> Work = _ => Task.FromResult(DeliveryOutcome.Inserted); public Task<DeliveryOutcome> HandleAsync(ReadOnlyMemory<byte> b, EventMetadata m, CancellationToken t) { Calls.Add("project"); return Work(t); } }
    [Fact]
    public async Task PoisonAcknowledgedOnlyAfterConfirmedQuarantine()
    {
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new ConfirmedQuarantine(accepted.Task); var wire = new Wire { Quarantine = publisher };
        await using var services = new ServiceCollection().AddSingleton<ISearchEventHandler>(new Handler()).BuildServiceProvider(); using var cancel = new CancellationTokenSource();
        var run = wire.Session(services).RunAsync(cancel.Token); await wire.Subscribed.Task;
        var delivery = wire.DeliverAsync(new BasicProperties { Headers = new Dictionary<string, object?> { { "schemaVersion", "invalid" } } }); await publisher.Entered.Task;
        Assert.Equal(0, wire.Acks); accepted.SetResult(); await delivery; Assert.Equal(1, wire.Acks); cancel.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }
    private sealed class ConfirmedQuarantine(Task confirm) : IQuarantinePublisher
    { public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); public async Task PublishAsync(QuarantineMessage message, CancellationToken token) { Assert.Equal("invalid_metadata", message.Code); Entered.SetResult(); await confirm.WaitAsync(token); } }
    private sealed class FailedQuarantine : IQuarantinePublisher { public Task PublishAsync(QuarantineMessage message, CancellationToken token) => Task.FromException(new IOException()); }
    private sealed class Session(Func<CancellationToken, Task> run) : IConsumerSession { public Task RunAsync(CancellationToken t) => run(t); }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? m, object?[]? args) => Call(m!, args!);
        public static T For<T>(Func<MethodInfo, object?[], object?> call) where T : class { var proxy = Create<T, Proxy>(); ((Proxy)(object)proxy).Call = call; return proxy; }
    }
    private sealed class Wire
    {
        public TaskCompletionSource Subscribed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public AsyncEventingBasicConsumer? Consumer; public AsyncEventHandler<ShutdownEventArgs>? Shutdown; public IChannel Channel = null!;
        public IQuarantinePublisher? Quarantine;
        public bool Open = true, AutoAck, Multiple, DurableQueue, DurableExchange; public ushort Prefetch; public ulong AckTag; public int Acks, ChannelDisposals, ConnectionDisposals; public string Failure = "", Binding = ""; public Exception? AckFailure; public CreateChannelOptions? ChannelOptions; public List<string> Calls = new();
        public Task DeliverAsync(BasicProperties p) => Consumer!.HandleBasicDeliverAsync("tag", 7, true, "exchange", "route", p, Body, default);
        public RabbitMqConsumerSession Session(IServiceProvider services, ILogger<RabbitMqConsumerSession>? logger = null)
        {
            Channel = Proxy.For<IChannel>((m, a) => m.Name switch
            {
                "get_IsOpen" => Open,
                "add_ChannelShutdownAsync" => SetShutdown(a),
                "ExchangeDeclareAsync" => Exchange(a),
                "QueueDeclareAsync" => Queue(a),
                "QueueBindAsync" => Bind(a),
                "BasicCancelAsync" => CancelConsumer(),
                "BasicQosAsync" => Qos(a),
                "BasicConsumeAsync" => Subscribe(a),
                "BasicAckAsync" => Ack(a),
                "DisposeAsync" => DisposeChannel(),
                _ => throw new NotSupportedException(m.Name)
            });
            var connection = Proxy.For<IConnection>((m, a) => m.Name switch { "CreateChannelAsync" => CreateChannel(a), "DisposeAsync" => DisposeConnection(), _ => throw new NotSupportedException(m.Name) });
            var factory = Proxy.For<IConnectionFactory>((m, a) => Failure == "connect" ? Task.FromException<IConnection>(new IOException()) : Task.FromResult(connection));
            return new(factory, Options.Create(new ConsumerOptions { DrainSeconds = 1 }), services.GetRequiredService<IServiceScopeFactory>(), logger ?? NullLogger<RabbitMqConsumerSession>.Instance, Quarantine ?? new FailedQuarantine(), new IngestionState(), TimeProvider.System);
        }
        private async Task CancelConsumer() { await Consumer!.HandleBasicCancelOkAsync("tag", default); await Shutdown!(Channel, new ShutdownEventArgs(ShutdownInitiator.Application, 200, "safe")); await DeliverAsync(new()); }
        private object? SetShutdown(object?[] a) { Shutdown = (AsyncEventHandler<ShutdownEventArgs>)a[0]!; return null; }
        private Task Exchange(object?[] a) { DurableExchange = (bool)a[2]!; return Failure == "topology" ? Task.FromException(new IOException()) : Task.CompletedTask; }
        private Task<QueueDeclareOk> Queue(object?[] a) { DurableQueue = (bool)a[1]!; return Task.FromResult(new QueueDeclareOk((string)a[0]!, 0, 0)); }
        private Task Bind(object?[] a) { Binding = (string)a[2]!; return Task.CompletedTask; }
        private Task Qos(object?[] a) { Prefetch = (ushort)a[1]!; return Task.CompletedTask; }
        private Task<string> Subscribe(object?[] a) { AutoAck = (bool)a[1]!; Consumer = (AsyncEventingBasicConsumer)a[6]!; Subscribed.TrySetResult(); return Task.FromResult("tag"); }
        private ValueTask Ack(object?[] a) { Acks++; AckTag = (ulong)a[0]!; Multiple = (bool)a[1]!; Calls.Add("ack"); return AckFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(AckFailure); }
        private Task<IChannel> CreateChannel(object?[] a) { ChannelOptions = (CreateChannelOptions)a[0]!; return Failure == "channel" ? Task.FromException<IChannel>(new IOException()) : Task.FromResult(Channel); }
        private ValueTask DisposeChannel() { ChannelDisposals++; return Failure == "dispose" ? ValueTask.FromException(new IOException("private")) : ValueTask.CompletedTask; }
        private ValueTask DisposeConnection() { ConnectionDisposals++; return Failure == "dispose" ? ValueTask.FromException(new IOException("private")) : ValueTask.CompletedTask; }
    }
}
