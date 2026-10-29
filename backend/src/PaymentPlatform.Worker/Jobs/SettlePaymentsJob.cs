using Microsoft.Extensions.Options;
using PaymentPlatform.Application.Settlement;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// Settles captured payments every minute (ADR 003), draining full batches. Runs are traced: once a minute is not
/// noise, and each batch's <c>payment.settled</c> events, and so their webhooks, join the run's trace.
/// </summary>
public sealed class SettlePaymentsJob(
    IServiceScopeFactory scopes, TimeProvider timeProvider, WorkerTelemetry telemetry, ILogger<SettlePaymentsJob> logger)
    : PeriodicJob(scopes, timeProvider, telemetry, logger)
{
    public override string Name => "settle-payments";

    public override TimeSpan Interval => TimeSpan.FromMinutes(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var settle = services.GetRequiredService<SettlePayments>();
        var batchSize = services.GetRequiredService<IOptions<SettlementOptions>>().Value.BatchSize;
        while (SettlePayments.CountOf(await settle.HandleAsync(cancellationToken)) == batchSize)
        {
        }
    }
}
