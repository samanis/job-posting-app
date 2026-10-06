using JobPosting.Api.Diagnostics;
using JobPosting.Api.Messaging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.CircuitBreaker;
namespace JobPosting.Api.Resilience;

public interface IPublisherProbe { Task ProbeAsync(CancellationToken cancellationToken); }
public sealed class PublicationCircuit : IJobEventPublisher, IPublisherProbe
{
    private readonly IJobEventPublisher publisher;
    private readonly IPublisherProbe probe;
    private readonly ResiliencePipeline pipeline;
    private readonly CircuitBreakerStateProvider state = new();
    public CircuitState State => state.CircuitState;
    public PublicationCircuit(IJobEventPublisher publisher, IPublisherProbe probe, IOptions<ResilienceOptions> options,
        TimeProvider clock, ILogger<PublicationCircuit> logger)
    {
        this.publisher = publisher; this.probe = probe;
        var settings = options.Value;
        pipeline = new ResiliencePipelineBuilder { TimeProvider = clock }.AddCircuitBreaker(new CircuitBreakerStrategyOptions
        {
            FailureRatio = settings.FailureRatio,
            SamplingDuration = TimeSpan.FromSeconds(settings.SamplingSeconds),
            MinimumThroughput = settings.MinimumThroughput,
            BreakDuration = TimeSpan.FromSeconds(settings.BreakSeconds),
            StateProvider = state,
            ShouldHandle = arguments => ValueTask.FromResult(arguments.Outcome.Exception is JobPublicationException),
            OnOpened = _ => Transition("open"),
            OnHalfOpened = _ => Transition("half_open"),
            OnClosed = _ => Transition("closed")
        }).Build();
        ValueTask Transition(string next)
        {
            PostingMetrics.Transition(next);
            logger.LogInformation(new EventId(3000, "PublicationCircuitTransition"), "Publication circuit {State}", next);
            return ValueTask.CompletedTask;
        }
    }
    public async Task PublishAsync(JobPostingCreated envelope, CancellationToken cancellationToken)
    {
        try
        {
            await pipeline.ExecuteAsync(async token => await publisher.PublishAsync(envelope, token), cancellationToken);
            PostingMetrics.Publication("confirmed");
        }
        catch (BrokenCircuitException exception)
        {
            PostingMetrics.Publication("circuit_open");
            throw new JobPublicationException(PublicationFailure.NotAccepted, exception);
        }
        catch (OperationCanceledException) { PostingMetrics.Publication("cancelled"); throw; }
        catch (Exception) { PostingMetrics.Publication("failed"); throw; }
    }
    public async Task ProbeAsync(CancellationToken cancellationToken)
    {
        try { await pipeline.ExecuteAsync(async token => await probe.ProbeAsync(token), cancellationToken); }
        catch (BrokenCircuitException exception) { throw new JobPublicationException(PublicationFailure.NotAccepted, exception); }
    }
}
