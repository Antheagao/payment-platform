namespace PaymentPlatform.Application.Webhooks;

/// <summary>
/// When a failed delivery is tried again (ADR 003): exponential backoff from 10 seconds, doubling to a cap of one
/// hour, with full jitter (a random delay between zero and the backoff), so receivers coming back from an outage
/// are not hit by every retry at once. After <see cref="MaxAttempts"/> attempts the delivery is dead-lettered;
/// the waits add up to at most about 3.4 hours, and half that on average.
/// </summary>
public static class WebhookRetrySchedule
{
    public const int MaxAttempts = 12;

    private static readonly TimeSpan First = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Cap = TimeSpan.FromHours(1);

    /// <summary>The longest wait after the given number of failed attempts, before jitter.</summary>
    public static TimeSpan Backoff(int failedAttempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(failedAttempts, 1);
        var doublings = Math.Min(failedAttempts - 1, 20);
        var backoff = First * Math.Pow(2, doublings);
        return backoff < Cap ? backoff : Cap;
    }

    /// <summary>When to try again after <paramref name="failedAttempts"/> failures, or null when the delivery is dead-lettered.</summary>
    /// <param name="jitter">A random fraction in [0, 1): <see cref="Random.NextDouble"/>.</param>
    public static DateTimeOffset? NextAttemptAt(int failedAttempts, DateTimeOffset now, double jitter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(jitter, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(jitter, 1);
        return failedAttempts >= MaxAttempts ? null : now + (Backoff(failedAttempts) * jitter);
    }
}
