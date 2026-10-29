using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// Sends due webhook deliveries every second (ADR 003), draining full batches until one comes back short, so a burst
/// of events is not held to one batch a second.
/// </summary>
public sealed class DeliverWebhooksJob(
    IServiceScopeFactory scopes, TimeProvider timeProvider, WorkerTelemetry telemetry, ILogger<DeliverWebhooksJob> logger)
    : PeriodicJob(scopes, timeProvider, telemetry, logger)
{
    public override string Name => "deliver-webhooks";

    public override TimeSpan Interval => TimeSpan.FromSeconds(1);

    // As for the dispatcher: each delivery is traced under the request that caused its event, not as a run.
    protected override bool TraceRuns => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var deliver = services.GetRequiredService<DeliverWebhooks>();
        while (await deliver.HandleAsync(cancellationToken) == DeliverWebhooks.BatchSize)
        {
        }
    }
}
