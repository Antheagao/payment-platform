using System.Diagnostics;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// The loop every background job runs in: one run at startup, then one per <see cref="Interval"/>, never
/// overlapping within an instance. Each run gets its own DI scope (so its own <c>DbContext</c>), a span, and a
/// duration sample; an exception fails that run only, is logged and counted, and the next tick runs as usual.
/// </summary>
/// <remarks>
/// Running several worker instances is safe only because each job claims its rows with
/// <c>UPDLOCK, READPAST</c> (ADR 003); the loop itself does no cross-instance coordination.
/// </remarks>
public abstract partial class PeriodicJob(
    IServiceScopeFactory scopes, TimeProvider timeProvider, WorkerTelemetry telemetry, ILogger logger)
    : BackgroundService
{
    /// <summary>A short kebab-case name, used in spans, metrics, and logs: <c>deliver-webhooks</c>.</summary>
    public abstract string Name { get; }

    public abstract TimeSpan Interval { get; }

    /// <summary>One unit of work, such as claiming and processing one batch.</summary>
    /// <param name="services">The run's own scope: resolve the <c>DbContext</c> and use cases from it.</param>
    protected abstract Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);
        LogStarted(logger, Name, Interval);
        do
        {
            await RunGuardedAsync(stoppingToken);
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    internal async Task RunGuardedAsync(CancellationToken stoppingToken)
    {
        using var activity = WorkerTelemetry.ActivitySource.StartActivity($"job {Name}");
        activity?.SetTag("job.name", Name);
        var started = timeProvider.GetTimestamp();
        var failed = false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await RunOnceAsync(scope.ServiceProvider, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception exception)
        {
            failed = true;
            activity?.SetStatus(ActivityStatusCode.Error);
            activity?.AddException(exception);
            LogRunFailed(logger, Name, exception);
        }
        finally
        {
            telemetry.RecordRun(Name, timeProvider.GetElapsedTime(started), failed);
        }
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobName} started, every {Interval}")]
    private static partial void LogStarted(ILogger logger, string jobName, TimeSpan interval);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobName} run failed; the next run is on schedule")]
    private static partial void LogRunFailed(ILogger logger, string jobName, Exception exception);
}
