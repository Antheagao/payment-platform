using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentPlatform.Application.Payments;
using PaymentPlatform.Application.Settlement;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>The asynchronous workflow's queues at one moment, as <see cref="MeasureQueues"/> reads them.</summary>
public sealed record QueueSnapshot(
    long OutboxPending,
    TimeSpan OutboxOldestAge,
    long DeliveriesDue,
    TimeSpan DeliveryLag,
    long DeliveriesRetrying,
    long DeliveriesDeadLettered,
    long SettlementDue,
    long ReconciliationDue);

/// <summary>
/// The <c>PaymentPlatform.Outbox</c> meter (R20): how deep and how late the asynchronous workflow is, and what its
/// deliveries end in. The gauges report the last <see cref="QueueSnapshot"/>, because a gauge's callback cannot query
/// a database; the worker refreshes it every few seconds, and a host that never measures reports nothing.
/// </summary>
public sealed class QueueMetrics
{
    public const string MeterName = "PaymentPlatform.Outbox";

    public const string OutcomeTag = "webhook.outcome";

    private readonly Counter<long> _attempts;
    private QueueSnapshot? _snapshot;

    public QueueMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        var meter = meterFactory.Create(MeterName);
        _attempts = meter.CreateCounter<long>(
            "webhook.delivery.attempts", unit: "{attempt}", description: "Webhook delivery attempts, by how they ended: delivered, retried, dead_lettered.");
        Gauge(meter, "outbox.messages.pending", "{message}", "Outbox messages not yet dispatched into deliveries.", snapshot => snapshot.OutboxPending);
        Gauge(meter, "outbox.messages.oldest_age", "s", "How long the oldest undispatched outbox message has waited.", snapshot => snapshot.OutboxOldestAge.TotalSeconds);
        Gauge(meter, "webhook.deliveries.due", "{delivery}", "Webhook deliveries due to be sent now.", snapshot => snapshot.DeliveriesDue);
        Gauge(meter, "webhook.deliveries.lag", "s", "How long the most overdue webhook delivery has been due: processing lag.", snapshot => snapshot.DeliveryLag.TotalSeconds);
        Gauge(meter, "webhook.deliveries.retrying", "{delivery}", "Webhook deliveries pending after at least one failed attempt.", snapshot => snapshot.DeliveriesRetrying);
        Gauge(meter, "webhook.deliveries.dead_lettered", "{delivery}", "Webhook deliveries that gave up, waiting for an operator.", snapshot => snapshot.DeliveriesDeadLettered);
        Gauge(meter, "settlement.payments.due", "{payment}", "Captured payments past the settlement delay, not yet settled.", snapshot => snapshot.SettlementDue);
        Gauge(meter, "reconciliation.payments.due", "{payment}", "Payments still Pending past the grace period, due to be asked about again.", snapshot => snapshot.ReconciliationDue);
    }

    public QueueSnapshot? Last => _snapshot;

    public void Update(QueueSnapshot snapshot) => _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));

    public void RecordAttempt(string outcome) => _attempts.Add(1, new KeyValuePair<string, object?>(OutcomeTag, outcome));

    private void Gauge<T>(Meter meter, string name, string unit, string description, Func<QueueSnapshot, T> read)
        where T : struct =>
        meter.CreateObservableGauge(
            name,
            () => _snapshot is { } snapshot ? [new Measurement<T>(read(snapshot))] : Array.Empty<Measurement<T>>(),
            unit,
            description);
}

/// <summary>Reads the queues' depth and age in a few small queries over their filtered indexes, and hands them to the gauges.</summary>
public sealed class MeasureQueues(
    PaymentsDbContext db,
    QueueMetrics metrics,
    IOptions<SettlementOptions> settlement,
    IOptions<ReconciliationOptions> reconciliation,
    TimeProvider timeProvider)
{
    public async Task<QueueSnapshot> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var outbox = db.OutboxMessages.AsNoTracking().Where(message => message.DispatchedAt == null);
        var pending = db.WebhookDeliveries.AsNoTracking().Where(delivery => delivery.Status == WebhookDeliveryStatus.Pending);
        var due = pending.Where(delivery => delivery.NextAttemptAt <= now);
        var capturedBefore = now - settlement.Value.Delay;

        var oldestMessage = await outbox.MinAsync(message => (DateTimeOffset?)message.OccurredAt, cancellationToken);
        var oldestDue = await due.MinAsync(delivery => (DateTimeOffset?)delivery.NextAttemptAt, cancellationToken);
        var snapshot = new QueueSnapshot(
            await outbox.LongCountAsync(cancellationToken),
            oldestMessage is { } occurred ? Positive(now - occurred) : TimeSpan.Zero,
            await due.LongCountAsync(cancellationToken),
            oldestDue is { } dueAt ? Positive(now - dueAt) : TimeSpan.Zero,
            await pending.LongCountAsync(delivery => delivery.Attempts > 0, cancellationToken),
            await db.WebhookDeliveries.AsNoTracking().LongCountAsync(delivery => delivery.Status == WebhookDeliveryStatus.DeadLettered, cancellationToken),
            await db.Payments.AsNoTracking().LongCountAsync(
                payment => payment.Status == Domain.Payments.PaymentStatus.Captured && payment.UpdatedAt < capturedBefore, cancellationToken),
            await ReconcilePendingPayments.Due(db.Payments.AsNoTracking(), now, reconciliation.Value.GracePeriod).LongCountAsync(cancellationToken));
        metrics.Update(snapshot);
        return snapshot;
    }

    private static TimeSpan Positive(TimeSpan age) => age > TimeSpan.Zero ? age : TimeSpan.Zero;
}
