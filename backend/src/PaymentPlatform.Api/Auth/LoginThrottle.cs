using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;

namespace PaymentPlatform.Api.Auth;

/// <summary>
/// The per-email login limit. ASP.NET Core's rate-limiting middleware partitions before the body is read, so it
/// holds the per-address limit; the email is only known inside the endpoint, which asks here.
/// </summary>
public sealed class LoginThrottle : IDisposable
{
    private readonly PartitionedRateLimiter<string> _limiter;

    public LoginThrottle(IOptions<LoginRateLimitOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var limits = options.Value;
        _limiter = PartitionedRateLimiter.Create<string, string>(email => RateLimitPartition.GetFixedWindowLimiter(
            email,
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = limits.PerEmailPermits,
                Window = limits.PerEmailWindow,
                QueueLimit = 0,
            }));
    }

    /// <summary>
    /// Takes one attempt for the email, without regard to case or surrounding space. When none are left, says how
    /// long until the window resets.
    /// </summary>
    public bool TryAcquire(string email, out TimeSpan retryAfter)
    {
        ArgumentNullException.ThrowIfNull(email);
        using var lease = _limiter.AttemptAcquire(email.Trim().ToUpperInvariant());
        retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var wait) ? wait : TimeSpan.Zero;
        return lease.IsAcquired;
    }

    public void Dispose() => _limiter.Dispose();
}
