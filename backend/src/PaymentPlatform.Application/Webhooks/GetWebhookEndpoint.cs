using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>The caller's merchant's webhook endpoint, without its secret.</summary>
public sealed class GetWebhookEndpoint(PaymentsDbContext db)
{
    public async Task<WebhookEndpointDetails?> HandleAsync(MerchantId merchantId, CancellationToken cancellationToken)
    {
        var endpoint = await db.WebhookEndpoints.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.MerchantId == merchantId, cancellationToken);
        return endpoint is null ? null : WebhookEndpointDetails.From(endpoint, withSecret: false);
    }
}
