using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Persistence;
using PaymentPlatform.ServiceDefaults;

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
    // Asks the sampler to drop a run's span and everything under it (TraceSampling).
    private static readonly KeyValuePair<string, object?>[] UntracedRunTags = [new(TraceSampling.PriorityTag, 0)];

    // Checked until it holds, then trusted: a new migration only ever arrives with a new build.
    private bool _schemaCurrent;

    /// <summary>A short kebab-case name, used in spans, metrics, and logs: <c>deliver-webhooks</c>.</summary>
    public abstract string Name { get; }

    public abstract TimeSpan Interval { get; }

    /// <summary>
    /// Whether each run is a trace of its own. A job that runs every few seconds turns it off: its runs still log,
    /// count, and time themselves, and the work they do for another trace is traced there.
    /// </summary>
    protected virtual bool TraceRuns => true;

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
        using var activity = WorkerTelemetry.ActivitySource.StartActivity(
            $"job {Name}", ActivityKind.Internal, parentContext: default, tags: TraceRuns ? null : UntracedRunTags);
        activity?.SetTag("job.name", Name);
        var started = timeProvider.GetTimestamp();
        var failed = false;
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            if (!_schemaCurrent && !(_schemaCurrent = await SchemaIsCurrentAsync(scope.ServiceProvider, stoppingToken)))
            {
                LogSchemaNotCurrent(logger, Name);
                return;
            }

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

    /// <summary>
    /// A job runs only against the schema this build carries, the rule readiness applies to traffic: at a fresh
    /// start the API may still be migrating, and a deployment migrates before the new build takes over.
    /// </summary>
    private static async Task<bool> SchemaIsCurrentAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetService<PaymentsDbContext>();
        return db is null || !(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Any();
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Job {JobName} skipped this run: the database has migrations to apply")]
    private static partial void LogSchemaNotCurrent(ILogger logger, string jobName);

    [LoggerMessage(Level = LogLevel.Error, Message = "Job {JobName} run failed; the next run is on schedule")]
    private static partial void LogRunFailed(ILogger logger, string jobName, Exception exception);
}
