using Microsoft.Extensions.Options;
using PaymentPlatform.Application.Payments;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// Finishes payments left <c>Pending</c> every minute, draining full batches. Runs are traced, as settlement's are:
/// once a minute is not noise, and each payment it moves, with its webhook, joins the run's trace.
/// </summary>
public sealed class ReconcilePendingPaymentsJob(
    IServiceScopeFactory scopes, TimeProvider timeProvider, WorkerTelemetry telemetry, ILogger<ReconcilePendingPaymentsJob> logger)
    : PeriodicJob(scopes, timeProvider, telemetry, logger)
{
    public override string Name => "reconcile-pending-payments";

    public override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var reconcile = services.GetRequiredService<ReconcilePendingPayments>();
        var batchSize = services.GetRequiredService<IOptions<ReconciliationOptions>>().Value.BatchSize;
        while ((await reconcile.HandleAsync(cancellationToken)).Count == batchSize)
        {
        }
    }
}
