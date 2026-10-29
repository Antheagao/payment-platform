using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// Measures the outbox, delivery, and settlement queues every ten seconds for the <c>PaymentPlatform.Outbox</c> gauges
/// (R20): often enough for a lag alert to fire within a minute, rare enough to cost nothing.
/// </summary>
public sealed class MeasureQueuesJob(
    IServiceScopeFactory scopes, TimeProvider timeProvider, WorkerTelemetry telemetry, ILogger<MeasureQueuesJob> logger)
    : PeriodicJob(scopes, timeProvider, telemetry, logger)
{
    public override string Name => "measure-queues";

    public override TimeSpan Interval => TimeSpan.FromSeconds(10);

    // Six traces a minute of small counts would be noise; the gauges are the output.
    protected override bool TraceRuns => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken) =>
        await services.GetRequiredService<MeasureQueues>().HandleAsync(cancellationToken);
}
