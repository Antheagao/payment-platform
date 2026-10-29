using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// Moves outbox messages into webhook deliveries every second (ADR 003), draining full batches until one comes back
/// short, so a burst does not wait a second per hundred messages.
/// </summary>
public sealed class DispatchOutboxJob(
    IServiceScopeFactory scopes, TimeProvider timeProvider, WorkerTelemetry telemetry, ILogger<DispatchOutboxJob> logger)
    : PeriodicJob(scopes, timeProvider, telemetry, logger)
{
    public override string Name => "dispatch-outbox";

    public override TimeSpan Interval => TimeSpan.FromSeconds(1);

    // A trace a second would bury the rest; each event's dispatch is traced under the request that wrote it.
    protected override bool TraceRuns => false;

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var dispatch = services.GetRequiredService<DispatchOutboxMessages>();
        while (await dispatch.HandleAsync(cancellationToken) == DispatchOutboxMessages.BatchSize)
        {
        }
    }
}
