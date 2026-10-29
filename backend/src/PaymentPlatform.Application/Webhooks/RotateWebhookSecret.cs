using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// Issues a new signing secret for the caller's merchant's endpoint and returns it once. Every delivery from then
/// on, retries of older events included, is signed with it; this is also how a lost secret is recovered.
/// </summary>
public sealed partial class RotateWebhookSecret(PaymentsDbContext db, TimeProvider timeProvider, ILogger<RotateWebhookSecret> logger)
{
    public async Task<Result<WebhookEndpointDetails>> HandleAsync(MerchantId merchantId, CancellationToken cancellationToken)
    {
        var endpoint = await db.WebhookEndpoints.SingleOrDefaultAsync(candidate => candidate.MerchantId == merchantId, cancellationToken);
        if (endpoint is null)
        {
            return WebhookEndpointErrors.NotFound;
        }

        endpoint.RotateSecret(WebhookSecrets.New(), timeProvider.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        LogRotated(logger, merchantId);
        return WebhookEndpointDetails.From(endpoint, withSecret: true);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook signing secret rotated for merchant {MerchantId}")]
    private static partial void LogRotated(ILogger logger, MerchantId merchantId);
}
