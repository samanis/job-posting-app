using System.Diagnostics.Metrics;
using JobPosting.Api.Diagnostics;
using JobPosting.Api.Messaging;
using JobPosting.Api.Persistence;
using JobPosting.Api.Resilience;
using JobPosting.Api.Tests.Support;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Polly.CircuitBreaker;
namespace JobPosting.Api.Tests;
[Trait("Category","Unit")]
public sealed class ResilienceTests
{
    private sealed class Dependency : IJobEventPublisher,IPublisherProbe,IDatabaseProbe
    {
        public int Sends,Probes; public Func<CancellationToken,Task> Action = _=>Task.CompletedTask; public bool Database=true;
        public async Task PublishAsync(JobPostingCreated e,CancellationToken t) { Sends++;await Action(t); }
        public async Task ProbeAsync(CancellationToken t) { Probes++; await Action(t); }
        public Task<bool> CheckAsync(CancellationToken t)=>Task.FromResult(Database);
    }
    private static JobPostingCreated Envelope()=>JobPostingCreated.FromSaved(PersistenceTests.Posting().Job,"trace");
    private static JobPublicationException Failure()=>new(PublicationFailure.NotAccepted,new IOException("secret"));
    [Fact]
    public async Task LowVolumeBreakerOpensAllowsOneProbeAndClosesWithoutReplay()
    {
        var clock=new FakeTimeProvider(); var dependency=new Dependency {Action=_=>Task.FromException(Failure())};
        using var logs=new CapturedLoggerProvider(); using var logging=LoggerFactory.Create(b=>b.AddProvider(logs));
        var circuit=new PublicationCircuit(dependency,dependency,Options.Create(new ResilienceOptions()),clock,logging.CreateLogger<PublicationCircuit>());
        for(var i=0;i<2;i++) await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.PublishAsync(Envelope(),default));
        Assert.Equal(CircuitState.Closed,circuit.State);
        await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.PublishAsync(Envelope(),default));
        Assert.Equal(CircuitState.Open,circuit.State);
        var rejected=await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.PublishAsync(Envelope(),default)); Assert.IsType<BrokenCircuitException>(rejected.InnerException);
        await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.ProbeAsync(default)); Assert.Equal(3,dependency.Sends); Assert.Equal(0,dependency.Probes);
        clock.Advance(TimeSpan.FromSeconds(15));
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);dependency.Action=_=>release.Task;
        var probe=circuit.ProbeAsync(default); Assert.Equal(CircuitState.HalfOpen,circuit.State);
        await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.PublishAsync(Envelope(),default));
        release.SetResult();await probe; Assert.Equal(CircuitState.Closed,circuit.State);
        Assert.Equal(new[]{"open","half_open","closed"},logs.Entries.Select(e=>(string)e.Properties["State"]!));
        await circuit.PublishAsync(Envelope(),default); Assert.Equal(4,dependency.Sends);
    }
    [Fact]
    public async Task RatioSamplingCancellationAndFailedHalfOpenHaveNoAutomaticRetries()
    {
        var clock=new FakeTimeProvider();var dependency=new Dependency();
        var circuit=new PublicationCircuit(dependency,dependency,Options.Create(new ResilienceOptions()),clock,NullLogger<PublicationCircuit>.Instance);
        await circuit.PublishAsync(Envelope(),default);
        dependency.Action=_=>Task.FromException(Failure());await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.PublishAsync(Envelope(),default));Assert.Equal(CircuitState.Closed,circuit.State);
        clock.Advance(TimeSpan.FromSeconds(61));
        for(var i=0;i<2;i++)await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.PublishAsync(Envelope(),default));Assert.Equal(CircuitState.Closed,circuit.State);
        using var cancelled=new CancellationTokenSource();cancelled.Cancel();dependency.Action=t=>Task.FromCanceled(t);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>circuit.PublishAsync(Envelope(),cancelled.Token));Assert.Equal(CircuitState.Closed,circuit.State);
        dependency.Action=_=>Task.FromException(Failure());await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.PublishAsync(Envelope(),default));Assert.Equal(CircuitState.Open,circuit.State);
        clock.Advance(TimeSpan.FromSeconds(15));await Assert.ThrowsAsync<JobPublicationException>(()=>circuit.ProbeAsync(default));Assert.Equal(CircuitState.Open,circuit.State);
        clock.Advance(TimeSpan.FromSeconds(15));dependency.Action=_=>Task.CompletedTask;await circuit.ProbeAsync(default);Assert.Equal(CircuitState.Closed,circuit.State);
    }
    [Theory]
    [InlineData(0,0,1,0)] [InlineData(1.1,61,1001,61)] [InlineData(double.NaN,1,2,1)] [InlineData(0.5,60,3,15)]
    public void OptionsRejectInvalidAndAcceptDefaults(double ratio,int sample,int throughput,int duration)
    { var result=new ResilienceOptionsValidator().Validate(null,new(){FailureRatio=ratio,SamplingSeconds=sample,MinimumThroughput=throughput,BreakSeconds=duration});Assert.Equal(ratio==0.5,result.Succeeded); }
    [Fact]
    public async Task DrainRejectsNewRequestsAndWaitsForAllWorkWithRemainingCleanupBudget()
    {
        var clock=new FakeTimeProvider();var drain=new ShutdownDrain(clock);Assert.False(drain.IsStopping);Assert.Equal(TimeSpan.FromSeconds(3),drain.CleanupBudget);
        Assert.True(drain.Enter());Assert.True(drain.Enter());drain.Exit();Assert.False(drain.IsStopping);
        drain.BeginStop();Assert.True(drain.IsStopping);Assert.False(drain.Enter());
        var wait=drain.WaitAsync(default);Assert.False(wait.IsCompleted);clock.Advance(TimeSpan.FromSeconds(28));Assert.Equal(TimeSpan.FromSeconds(2),drain.CleanupBudget);
        clock.Advance(TimeSpan.FromSeconds(3));Assert.Equal(TimeSpan.Zero,drain.CleanupBudget);drain.Exit();await wait;
        var empty=new ShutdownDrain(clock);await empty.WaitAsync(default);Assert.True(empty.IsStopping);
    }
    private sealed class Lifetime : IHostApplicationLifetime
    { public CancellationTokenSource Stopping=new();public CancellationToken ApplicationStarted=>default;public CancellationToken ApplicationStopping=>Stopping.Token;public CancellationToken ApplicationStopped=>default;public void StopApplication()=>Stopping.Cancel(); }
    [Fact]
    public async Task HostedLifetimeAndMiddlewareDrainOnStopEvenWhenRequestThrows()
    {
        var drain=new ShutdownDrain(TimeProvider.System);var lifetime=new Lifetime();using var service=new DrainLifetime(drain,lifetime);await service.StartAsync(default);
        var middleware=new DrainMiddleware(_=>throw new IOException(),drain);await Assert.ThrowsAsync<IOException>(()=>middleware.InvokeAsync(new DefaultHttpContext()));
        lifetime.StopApplication();var context=new DefaultHttpContext();await middleware.InvokeAsync(context);Assert.Equal(503,context.Response.StatusCode);await service.StopAsync(default);
    }
    [Theory] [InlineData("success")] [InlineData("database")] [InlineData("broker")] [InlineData("stopping")] [InlineData("timeout")]
    public async Task HealthIsBoundedAndLiveHasNoDependencyIo(string mode)
    {
        var drain=new ShutdownDrain(TimeProvider.System);var dependency=new Dependency();
        if(mode=="database")dependency.Database=false;
        if(mode=="broker")dependency.Action=_=>Task.FromException(Failure());
        if(mode=="timeout")dependency.Action=t=>Task.Delay(Timeout.Infinite,t);
        if(mode=="stopping")drain.BeginStop();
        var controller=new HealthController(dependency,dependency,drain);
        Assert.IsType<OkObjectResult>(controller.Live());Assert.Equal(0,dependency.Probes);
        var result=Assert.IsAssignableFrom<ObjectResult>(await controller.Ready(default));Assert.Equal(mode=="success"?200:503,result.StatusCode);
    }
    [Fact]
    public async Task WorkflowCleanupFitsRemainingShutdownBudget()
    {
        using var harness=new PostingWorkflowTests.Harness();var clock=new FakeTimeProvider(new DateTimeOffset(2026,10,5,0,0,0,TimeSpan.Zero));var drain=new ShutdownDrain(clock);
        drain.BeginStop();clock.Advance(TimeSpan.FromSeconds(31));
        harness.Publish=(_,_)=>Task.FromException(Failure());harness.Delete=async token=>{await Task.Delay(Timeout.Infinite,token);return true;};
        var workflow=new JobPosting.Api.Posting.PostingWorkflow(new(harness,new(),new(clock,TimeZoneInfo.Utc),clock),harness,harness,clock,NullLogger<JobPosting.Api.Posting.PostingWorkflow>.Instance,drain);
        var error=await Assert.ThrowsAsync<JobPosting.Api.Posting.PostingWorkflowException>(()=>workflow.SubmitAsync(["key"],PostingWorkflowTests.Request(),"trace",default));
        Assert.Equal(JobPosting.Api.Contracts.JobApiProblems.PublicationUnresolved,error.Code);
    }
    private sealed class Factory(bool capable) : IDbContextFactory<PostingDbContext> { public PostingDbContext CreateDbContext()=>new Context(capable); }
    private sealed class Context(bool capable) : PostingDbContext(PostingPersistence.CreateOptions(new()))
    { public override DatabaseFacade Database=>new Database(this,capable); }
    private sealed class Database(DbContext context,bool capable) : DatabaseFacade(context)
    { public override Task<bool> CanConnectAsync(CancellationToken token=default)=>Task.FromResult(capable); }
    [Theory] [InlineData(false)] [InlineData(true)]
    public async Task DatabaseCapabilityChecksUseFreshDisposableContext(bool capable)=>Assert.Equal(capable,await new DatabaseProbe(new Factory(capable)).CheckAsync(default));
    [Fact]
    public void MetricsExposeOnlyBoundedLabels()
    {
        var captured=new List<(string,string)>();using var listener=new MeterListener();
        listener.InstrumentPublished=(instrument,meter)=>{if(instrument.Meter.Name==PostingMetrics.MeterName)meter.EnableMeasurementEvents(instrument);};
        listener.SetMeasurementEventCallback<long>((instrument,value,tags,_)=>{foreach(var tag in tags)captured.Add((instrument.Name,tag.Key));});
        listener.SetMeasurementEventCallback<double>((instrument,value,tags,_)=>{foreach(var tag in tags)captured.Add((instrument.Name,tag.Key));});listener.Start();
        PostingMetrics.Request(202,1);PostingMetrics.Publication("confirmed");PostingMetrics.Cleanup("deleted");PostingMetrics.Transition("closed");
        Assert.Contains(captured,item=>item.Item1=="jobposting.requests");Assert.All(captured,item=>Assert.Contains(item.Item2,new[]{"status_class","outcome","state"}));
    }
}
