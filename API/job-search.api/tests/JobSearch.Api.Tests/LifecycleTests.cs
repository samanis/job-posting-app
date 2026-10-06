using JobSearch.Api.Messaging;
using JobSearch.Api.Diagnostics;
using JobSearch.Api.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Reflection;
namespace JobSearch.Api.Tests;

[Trait("Category", "Unit")]
public sealed class LifecycleTests
{
    [Fact]
    public async Task DrainRejectsNewWorkAndWaitsForEveryAdmittedDelivery()
    {
        var empty = new SessionDrain(); Assert.True(empty.Stop().IsCompleted); Assert.False(empty.TryEnter());
        var drain = new SessionDrain(); Assert.True(drain.TryEnter()); Assert.True(drain.TryEnter()); var pending = drain.Stop(); Assert.False(pending.IsCompleted); Assert.False(drain.TryEnter()); drain.Exit(); Assert.False(pending.IsCompleted); drain.Exit(); await pending;
    }
    [Fact]
    public void MetricsExposeOnlyBoundedDimensions()
    {
        var names = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(); using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) => { if (instrument.Meter.Name == SearchMetrics.MeterName) l.EnableMeasurementEvents(instrument); };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => { names.TryAdd(instrument.Name, 0); foreach (var tag in tags) Assert.Contains(tag.Key, new[] { "outcome", "state" }); });
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => { names.TryAdd(instrument.Name, 0); foreach (var tag in tags) { Assert.Equal("status_class", tag.Key); Assert.InRange((int)tag.Value!, 1, 5); } }); listener.Start();
        SearchMetrics.Redelivery(); SearchMetrics.Projection(DeliveryOutcome.Inserted); SearchMetrics.Quarantine("confirmed"); SearchMetrics.Connection(ConsumerState.Running); SearchMetrics.Request(202, 5); Assert.Equal(5, names.Count);
    }
    [Fact]
    public void BackoffAndClassificationAreBounded()
    {
        var o = new ConsumerOptions(); Assert.Equal(.8, ConsumerFailures.Delay(0, o, 0).TotalSeconds); Assert.Equal(1, ConsumerFailures.Delay(0, o, 1).TotalSeconds); Assert.Equal(30, ConsumerFailures.Delay(100, o, 1).TotalSeconds);
        Assert.InRange(new BackoffJitter().Next(), 0, 1);
        foreach (var pair in new (Exception, string)[] { (new ProjectionCommitUncertainException(new IOException()), "commit_uncertain"), (new DbUpdateException(), "database"), (new Npgsql.NpgsqlException(), "database"), (new InvalidOperationException("wrapped", new Npgsql.NpgsqlException()), "database"), (new InvalidOperationException("unexpected", new IOException()), "unexpected"), (new QuarantineException(new IOException()), "quarantine"), (new OperationCanceledException(), "cancelled"), (new IOException(), "transport"), (new RabbitMQ.Client.Exceptions.BrokerUnreachableException(new IOException()), "transport"), (new Exception(), "unexpected") }) Assert.Equal(pair.Item2, ConsumerFailures.Classify(pair.Item1));
        SearchMetrics.Redelivery(); SearchMetrics.Projection(DeliveryOutcome.Duplicate); SearchMetrics.Quarantine("failed"); SearchMetrics.Request(503, 1);
    }
    [Fact]
    public async Task StableSessionResetsFailureBackoff()
    {
        using var stop = new CancellationTokenSource(); var clock = new LongClock();
        using var worker = new SearchConsumerWorker(new FailingSession(stop), Options.Create(new ConsumerOptions()), clock, NullLogger<SearchConsumerWorker>.Instance, new IngestionState(), new BackoffJitter()); await worker.RunAsync(stop.Token);
    }
    private sealed class LongClock : TimeProvider { private long stamp; public override long TimestampFrequency => 1; public override long GetTimestamp() => Interlocked.Add(ref stamp, 31); }
    private sealed class FailingSession(CancellationTokenSource stop) : IConsumerSession { public Task RunAsync(CancellationToken token) { stop.Cancel(); return Task.FromException(new IOException()); } }
    [Fact]
    public void ConfigurationRejectsUnsafeLifecycleBudgets()
    {
        var validator = new ConsumerOptionsValidator();
        foreach (var name in new[] { "QuarantineBudgetSeconds", "DrainSeconds", "ReconnectInitialSeconds", "ReconnectMaximumSeconds" }) foreach (var value in new[] { 0, 31 }) { var o = new ConsumerOptions(); typeof(ConsumerOptions).GetProperty(name)!.SetValue(o, value); Assert.True(validator.Validate(null, o).Failed); }
        Assert.True(validator.Validate(null, new ConsumerOptions { QuarantineExchange = "job-post-exchange" }).Failed);
        Assert.True(validator.Validate(null, new ConsumerOptions { QuarantineQueue = "job-post-queue" }).Failed);
        Assert.True(validator.Validate(null, new ConsumerOptions { ReconnectInitialSeconds = 10, ReconnectMaximumSeconds = 5 }).Failed);
        Assert.True(validator.Validate(null, new ConsumerOptions { QuarantineExchange = "", QuarantineQueue = "amq.reserved", QuarantineRoutingKey = new string('x', 256) }).Failed);
    }
    [Theory]
    [InlineData(true, false, 200)]
    [InlineData(false, false, 503)]
    [InlineData(false, true, 503)]
    public async Task ReadReadinessIsIndependentOfBroker(bool ready, bool fail, int expected)
    {
        var state = new IngestionState(); state.Set(ConsumerState.Backoff);
        var controller = new HealthController(new Probe(ready, fail), state, TimeProvider.System, NullLogger<HealthController>.Instance);
        Assert.IsType<OkObjectResult>(controller.Live()); Assert.Equal(503, Assert.IsType<ObjectResult>(controller.Ingestion()).StatusCode);
        Assert.Equal(expected, Assert.IsType<StatusCodeResult>(await controller.Ready(default)).StatusCode);
        state.Set(ConsumerState.Running); Assert.Equal(200, Assert.IsType<ObjectResult>(controller.Ingestion()).StatusCode);
        foreach (var stopping in new[] { ConsumerState.Draining, ConsumerState.Stopped }) { state.Set(stopping); Assert.Equal(503, Assert.IsType<StatusCodeResult>(await controller.Ready(default)).StatusCode); }
    }
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task QuarantineConfirmsBeforeReturningAndPreservesBody(bool publishFailure, bool cleanupFailure)
    {
        var body = new byte[] { 0, 255, 42 }; bool published = false; int disposed = 0;
        var channel = Proxy.For<IChannel>((m, a) => m.Name switch
        {
            "ExchangeDeclareAsync" or "QueueBindAsync" => Task.CompletedTask,
            "QueueDeclareAsync" => Task.FromResult(new QueueDeclareOk("quarantine", 0, 0)),
            "BasicPublishAsync" => Publish(a),
            "DisposeAsync" => Dispose(),
            _ => throw new NotSupportedException(m.Name)
        });
        var connection = Proxy.For<IConnection>((m, a) => m.Name == "CreateChannelAsync" ? Task.FromResult(channel) : Dispose());
        var factory = Proxy.For<IConnectionFactory>((m, a) => Task.FromResult(connection));
        var publisher = new RabbitMqQuarantinePublisher(factory, Options.Create(new ConsumerOptions()), TimeProvider.System, NullLogger<RabbitMqQuarantinePublisher>.Instance);
        if (publishFailure) await Assert.ThrowsAsync<QuarantineException>(() => publisher.PublishAsync(new(body, "invalid_event", "event", "trace", "application/json"), default));
        else await publisher.PublishAsync(new(body, "invalid_event", "event", "trace", "application/json"), default);
        Assert.True(published); Assert.Equal(2, disposed);
        ValueTask Publish(object?[] a) { Assert.True((bool)a[2]!); var p = Assert.IsType<BasicProperties>(a[3]); Assert.True(p.Persistent); Assert.Equal("event", p.MessageId); Assert.Equal("trace", p.CorrelationId); Assert.Equal("invalid_event", p.Headers!["failureCode"]); Assert.Equal(body, ((ReadOnlyMemory<byte>)a[4]!).ToArray()); published = true; return publishFailure ? ValueTask.FromException(new IOException("secret")) : ValueTask.CompletedTask; }
        ValueTask Dispose() { disposed++; return cleanupFailure ? ValueTask.FromException(new IOException("secret")) : ValueTask.CompletedTask; }
    }
    private sealed class Probe(bool ready, bool fail) : IDatabaseProbe { public Task<bool> ReadyAsync(CancellationToken token) => fail ? Task.FromException<bool>(new IOException("secret")) : Task.FromResult(ready); }
    public class Proxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> Call = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args!);
        public static T For<T>(Func<MethodInfo, object?[], object?> call) where T : class { var value = Create<T, Proxy>(); ((Proxy)(object)value).Call = call; return value; }
    }
}
