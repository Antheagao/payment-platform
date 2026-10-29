using System.ComponentModel.DataAnnotations;

namespace PaymentPlatform.Api.Auth;

/// <summary>
/// Login attempts allowed per email and per client address in a fixed window (ADR 004), from
/// <c>Auth:LoginRateLimit</c>. Successes count too, so the per-email limit also caps how fast one stolen password
/// can be tried across a botnet.
/// </summary>
public sealed class LoginRateLimitOptions
{
    public const string Section = "Auth:LoginRateLimit";

    [Range(1, 10_000)]
    public int PerEmailPermits { get; set; } = 5;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PerEmailWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Generous, because every dashboard login reaches the API from the dashboard's server; the per-address limit
    /// starts working per person once the dashboard forwards the client address (T26).
    /// </summary>
    [Range(1, 10_000)]
    public int PerAddressPermits { get; set; } = 60;

    [Range(typeof(TimeSpan), "00:00:01", "01:00:00")]
    public TimeSpan PerAddressWindow { get; set; } = TimeSpan.FromMinutes(1);
}
