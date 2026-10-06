namespace JobPosting.Api.Resilience;

public sealed class ShutdownDrain(TimeProvider clock)
{
    private readonly object gate = new();
    private int active;
    private long? stoppingAt;
    private TaskCompletionSource drained = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public bool IsStopping { get { lock (gate) return stoppingAt.HasValue; } }
    public TimeSpan CleanupBudget
    {
        get
        {
            lock (gate)
            {
                if (!stoppingAt.HasValue) return TimeSpan.FromSeconds(3);
                var remaining = TimeSpan.FromSeconds(30) - clock.GetElapsedTime(stoppingAt.Value);
                return TimeSpan.FromTicks(Math.Clamp(remaining.Ticks, 0, TimeSpan.FromSeconds(3).Ticks));
            }
        }
    }
    public bool Enter() { lock (gate) { if (stoppingAt.HasValue) return false; active++; return true; } }
    public void Exit() { lock (gate) { active--; if (active == 0 && stoppingAt.HasValue) drained.TrySetResult(); } }
    public void BeginStop() { lock (gate) { stoppingAt ??= clock.GetTimestamp(); if (active == 0) drained.TrySetResult(); } }
    public async Task WaitAsync(CancellationToken token) { BeginStop(); await drained.Task.WaitAsync(token); }
}
public sealed class DrainLifetime(ShutdownDrain drain, IHostApplicationLifetime lifetime) : IHostedService, IDisposable
{
    private CancellationTokenRegistration registration;
    public Task StartAsync(CancellationToken token) { registration = lifetime.ApplicationStopping.Register(drain.BeginStop); return Task.CompletedTask; }
    public Task StopAsync(CancellationToken token) => drain.WaitAsync(token);
    public void Dispose() => registration.Dispose();
}
public sealed class DrainMiddleware(RequestDelegate next, ShutdownDrain drain)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (!drain.Enter()) { context.Response.StatusCode = 503; return; }
        try { await next(context); }
        finally { drain.Exit(); }
    }
}
