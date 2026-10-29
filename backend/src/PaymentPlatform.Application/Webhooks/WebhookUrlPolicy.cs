using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// The SSRF rules for a merchant's webhook URL (security overlay): an absolute <c>https</c> URL with no user info or
/// fragment, whose host resolves only to public addresses. They are checked when the URL is set, for a clear answer,
/// and again for every connection the delivery makes (<see cref="ResolveAllowedAsync"/>), because a name can resolve
/// somewhere else by then.
/// </summary>
public sealed class WebhookUrlPolicy(IHostResolver resolver, IOptions<WebhookOptions> options)
{
    public const string Required = "url_required";
    public const string TooLong = "webhook_url_too_long";
    public const string Invalid = "webhook_url_invalid";
    public const string NotHttps = "webhook_url_not_https";
    public const string NotPublic = "webhook_url_not_public";
    public const string Unresolvable = "webhook_url_unresolvable";

    private static readonly TimeSpan ResolveTimeout = TimeSpan.FromSeconds(3);

    /// <summary>The error code for a URL that breaks a rule, or null for one a webhook may be sent to.</summary>
    public async Task<string?> CheckAsync(string? url, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Required;
        }

        if (url.Length > WebhookEndpoint.UrlMaxLength)
        {
            return TooLong;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("https" or "http")
            || uri.UserInfo.Length > 0
            || uri.Fragment.Length > 0
            || uri.IdnHost.Length == 0)
        {
            return Invalid;
        }

        if (IsLocalReceiver(uri.IdnHost))
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return NotHttps;
        }

        var addresses = await TryResolveAsync(uri.IdnHost, cancellationToken);
        return addresses.Length == 0 ? Unresolvable
            : addresses.All(WebhookAddresses.IsPublic) ? null
            : NotPublic;
    }

    /// <summary>
    /// The addresses a delivery may connect to for <paramref name="host"/>: none unless every address it resolves to is
    /// public, so a name that mixes a public address with a private one is refused rather than tried in turn.
    /// </summary>
    public async Task<IPAddress[]> ResolveAllowedAsync(string host, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);
        var addresses = await TryResolveAsync(host, cancellationToken);
        return IsLocalReceiver(host) || addresses.All(WebhookAddresses.IsPublic) ? addresses : [];
    }

    private bool IsLocalReceiver(string host) =>
        options.Value.LocalReceiverHosts.Contains(host, StringComparer.OrdinalIgnoreCase);

    private async Task<IPAddress[]> TryResolveAsync(string host, CancellationToken cancellationToken)
    {
        // An address literal needs no lookup, and is judged as it is.
        if (IPAddress.TryParse(host.Trim('[', ']'), out var literal))
        {
            return [literal];
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ResolveTimeout);
        try
        {
            return await resolver.ResolveAsync(host, timeout.Token);
        }
        catch (Exception exception) when (exception is SocketException or ArgumentException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            return [];
        }
    }
}
