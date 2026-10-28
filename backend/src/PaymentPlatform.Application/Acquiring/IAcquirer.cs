namespace PaymentPlatform.Application.Acquiring;

/// <summary>
/// The card network, as the platform sees it: authorize, capture, and refund a payment.
/// </summary>
/// <remarks>
/// The payment id is the acquirer-side idempotency key: calling again for the same payment returns the
/// original outcome and never moves money twice. That is what lets a request be retried and a payment
/// stuck in <c>Pending</c> be reconciled by authorizing again (ADR 007).
/// Implementations return <see cref="AcquirerDecision.Unavailable"/> for timeouts and outages rather than
/// throwing, so callers handle every outcome in one place.
/// </remarks>
public interface IAcquirer
{
    Task<AcquirerResponse> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken);

    Task<AcquirerResponse> CaptureAsync(FollowUpRequest request, CancellationToken cancellationToken);

    Task<AcquirerResponse> RefundAsync(FollowUpRequest request, CancellationToken cancellationToken);
}
