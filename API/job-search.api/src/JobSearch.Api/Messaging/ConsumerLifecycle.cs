using Microsoft.EntityFrameworkCore;
using Npgsql;
using JobSearch.Api.Persistence;
using JobSearch.Api.Diagnostics;
namespace JobSearch.Api.Messaging;
public enum ConsumerState { Disabled, Connecting, Running, Backoff, Draining, Stopped }
public sealed class IngestionState
{
    private int value=(int)ConsumerState.Connecting;
    public ConsumerState Current => (ConsumerState)Volatile.Read(ref value);
    public void Set(ConsumerState next)
    {
        Interlocked.Exchange(ref value,(int)next);
        SearchMetrics.Connection(next);
    }
}
public interface IBackoffJitter { double Next(); }
public sealed class BackoffJitter : IBackoffJitter { public double Next()=>Random.Shared.NextDouble(); }
public static class ConsumerFailures
{
    public static string Classify(Exception ex) => ex switch
    {
        ProjectionCommitUncertainException=>"commit_uncertain",
        DbUpdateException or NpgsqlException or InvalidOperationException {InnerException:NpgsqlException}=>"database",
        QuarantineException=>"quarantine",
        OperationCanceledException=>"cancelled",
        IOException or RabbitMQ.Client.Exceptions.BrokerUnreachableException=>"transport",
        _=>"unexpected"
    };
    public static TimeSpan Delay(int attempt,ConsumerOptions options,double jitter) => TimeSpan.FromSeconds(
        Math.Min(options.ReconnectMaximumSeconds,options.ReconnectInitialSeconds*Math.Pow(2,Math.Min(attempt,30)))*(0.8+0.2*jitter));
}
public sealed class SessionDrain
{
    private readonly object sync=new();
    private bool accepting=true;
    private int active;
    private TaskCompletionSource empty=Completed();
    private static TaskCompletionSource Completed(){var value=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);value.SetResult();return value;}
    public bool TryEnter()
    {
        lock(sync){if(!accepting)return false;if(active++==0)empty=new(TaskCreationOptions.RunContinuationsAsynchronously);return true;}
    }
    public void Exit()
    {
        lock(sync){if(--active==0)empty.TrySetResult();}
    }
    public Task Stop()
    {
        lock(sync){accepting=false;return empty.Task;}
    }
}
