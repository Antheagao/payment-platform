using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Application.Acquiring;

/// <summary>A capture or refund against an earlier authorization.</summary>
/// <param name="AuthorizationReference">The reference the acquirer returned when it approved the authorization.</param>
public sealed record FollowUpRequest(PaymentId PaymentId, Money Amount, string AuthorizationReference);
