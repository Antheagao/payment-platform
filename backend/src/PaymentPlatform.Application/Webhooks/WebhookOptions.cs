using Microsoft.Extensions.Configuration;

namespace PaymentPlatform.Application.Webhooks;

/// <summary>The <c>Webhooks</c> configuration section.</summary>
public sealed class WebhookOptions
{
    public const string Section = "Webhooks";

    /// <summary>
    /// Host names a webhook may reach over plain HTTP and on a private address, for the local receiver in compose.
    /// The hosts refuse the setting outside Development (<see cref="ThrowIfLocalReceiversOutsideDevelopment"/>): it
    /// switches the SSRF rules off for the names it lists.
    /// </summary>
    public IReadOnlyList<string> LocalReceiverHosts { get; set; } = [];

    /// <summary>Called by each host at startup, which knows its environment.</summary>
    public static void ThrowIfLocalReceiversOutsideDevelopment(IConfiguration configuration, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var hosts = configuration.GetSection($"{Section}:{nameof(LocalReceiverHosts)}").Get<string[]>() ?? [];
        if (hosts.Length > 0 && !isDevelopment)
        {
            throw new InvalidOperationException(
                $"{Section}:{nameof(LocalReceiverHosts)} is refused outside Development: it lets webhooks reach private addresses.");
        }
    }
}
