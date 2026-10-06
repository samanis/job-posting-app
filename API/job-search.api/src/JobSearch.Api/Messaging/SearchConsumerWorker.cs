using Microsoft.Extensions.Options;
namespace JobSearch.Api.Messaging;
public interface IConsumerSession { Task RunAsync(CancellationToken token); }
public sealed class SearchConsumerWorker(IConsumerSession session,IOptions<ConsumerOptions> options,TimeProvider clock,ILogger<SearchConsumerWorker> logger,IngestionState state,IBackoffJitter jitter):BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken token)=>RunAsync(token);
    public async Task RunAsync(CancellationToken token)
    {
        if(!options.Value.Enabled){state.Set(ConsumerState.Disabled);return;}
        var attempt=0;
        try
        {
            while(!token.IsCancellationRequested)
            {
                state.Set(ConsumerState.Connecting);
                var started=clock.GetTimestamp();
                try { await session.RunAsync(token);attempt=0; }
                catch(OperationCanceledException) when(token.IsCancellationRequested){return;}
                catch(Exception ex)
                {
                    if(clock.GetElapsedTime(started)>=TimeSpan.FromSeconds(30))attempt=0;
                    logger.LogWarning(new EventId(4100,"ConsumerSessionFailed"),"Consumer session ended; classification {Classification}; failure {Failure}",ConsumerFailures.Classify(ex),ex.GetType().Name);
                }
                state.Set(ConsumerState.Backoff);
                var delay=ConsumerFailures.Delay(attempt,options.Value,jitter.Next());attempt=Math.Min(30,attempt+1);
                try { await Task.Delay(delay,clock,token); }
                catch(OperationCanceledException){return;}
            }
        }
        finally{state.Set(ConsumerState.Stopped);}
    }
}
