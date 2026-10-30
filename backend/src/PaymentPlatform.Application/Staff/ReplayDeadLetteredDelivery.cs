using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Application.Webhooks;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Staff;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Staff;

/// <summary>
/// Puts a dead-lettered webhook delivery back on the queue (ADR 003, ADR 011), once an operator has seen the
/// merchant's receiver fixed: due now, with the whole retry schedule again. Only a dead-lettered one, and only to an
/// endpoint the merchant still has enabled; the worker's next run sends it.
/// </summary>
public sealed partial class ReplayDeadLetteredDelivery(
    PaymentsDbContext db, QueueMetrics metrics, TimeProvider timeProvider, ILogger<ReplayDeadLetteredDelivery> logger)
{
    public static readonly DomainError NotFound = new("not_found", "no webhook delivery with this id");

    public static readonly DomainError NotDeadLettered = new(
        "delivery_not_dead_lettered", "only a dead-lettered delivery can be replayed; this one is still on the queue or was delivered");

    public static readonly DomainError EndpointDisabled = new(
        "webhook_endpoint_disabled", "the merchant's webhook endpoint is disabled; a replay would be dead-lettered again unsent");

    public async Task<Result<WebhookDelivery>> HandleAsync(long deliveryId, StaffUserId staffId, CancellationToken cancellationToken)
    {
        var delivery = await db.WebhookDeliveries.SingleOrDefaultAsync(candidate => candidate.Id == deliveryId, cancellationToken);
        if (delivery is null)
        {
            return NotFound;
        }

        if (delivery.Status != WebhookDeliveryStatus.DeadLettered)
        {
            return NotDeadLettered;
        }

        var enabled = await db.WebhookEndpoints.AsNoTracking()
            .Where(endpoint => endpoint.Id == delivery.EndpointId)
            .Select(endpoint => endpoint.Enabled)
            .SingleAsync(cancellationToken);
        if (!enabled)
        {
            return EndpointDisabled;
        }

        delivery.Replay(timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        metrics.RecordReplay();
        LogReplayed(logger, delivery.Id, delivery.MerchantId.Value, staffId);
        return delivery;
    }

    // Which operator put traffic back on the wire is worth a line of its own, for the record of staff actions.
    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook delivery {DeliveryId} of merchant {MerchantId} replayed by staff user {StaffId}")]
    private static partial void LogReplayed(ILogger logger, long deliveryId, Guid merchantId, StaffUserId staffId);
}
