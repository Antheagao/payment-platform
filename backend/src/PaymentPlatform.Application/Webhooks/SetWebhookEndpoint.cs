using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <param name="Created">True when this call made the endpoint, and so issued its secret.</param>
public sealed record SetWebhookEndpointResult(WebhookEndpointDetails Endpoint, bool Created);

/// <summary>
/// Sets where the caller's merchant receives events (one endpoint per merchant, assumption 11). The first set issues
/// the signing secret and returns it once; later sets change the URL or turn the endpoint on or off and keep the
/// secret, so the call is safe to repeat. The URL must pass <see cref="WebhookUrlPolicy"/>.
/// </summary>
public sealed partial class SetWebhookEndpoint(
    PaymentsDbContext db, WebhookUrlPolicy policy, TimeProvider timeProvider, ILogger<SetWebhookEndpoint> logger)
{
    /// <summary>The URL's error code as a field error, or the endpoint as it now is.</summary>
    public async Task<(SetWebhookEndpointResult? Result, string? UrlError)> HandleAsync(
        MerchantId merchantId, string? url, bool enabled, CancellationToken cancellationToken)
    {
        if (await policy.CheckAsync(url, cancellationToken) is { } error)
        {
            return (null, error);
        }

        try
        {
            return (await SetAsync(merchantId, url!, enabled, cancellationToken), null);
        }
        catch (DbUpdateException exception) when (DatabaseErrors.IsDuplicateKey(exception))
        {
            // Another request made the endpoint first; this one is then an update of it.
            db.ChangeTracker.Clear();
            return (await SetAsync(merchantId, url!, enabled, cancellationToken), null);
        }
    }

    private async Task<SetWebhookEndpointResult> SetAsync(MerchantId merchantId, string url, bool enabled, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var endpoint = await db.WebhookEndpoints.SingleOrDefaultAsync(candidate => candidate.MerchantId == merchantId, cancellationToken);
        var created = endpoint is null;
        if (endpoint is null)
        {
            endpoint = WebhookEndpoint.Create(Guid.CreateVersion7(now), merchantId, url, WebhookSecrets.New(), now);
            if (!enabled)
            {
                endpoint.Disable(now);
            }

            db.WebhookEndpoints.Add(endpoint);
        }
        else
        {
            endpoint.Update(url, enabled, now);
        }

        await db.SaveChangesAsync(cancellationToken);

        // The host only: a path can carry a merchant's own token, and the secret is never logged.
        var host = new Uri(url).IdnHost;
        LogSet(logger, merchantId, host, enabled, created);
        return new SetWebhookEndpointResult(WebhookEndpointDetails.From(endpoint, withSecret: created), created);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Webhook endpoint for merchant {MerchantId} set to host {Host}, enabled {Enabled}, created {Created}")]
    private static partial void LogSet(ILogger logger, MerchantId merchantId, string host, bool enabled, bool created);
}
