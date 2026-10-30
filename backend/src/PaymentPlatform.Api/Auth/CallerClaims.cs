using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Staff;

namespace PaymentPlatform.Api.Auth;

/// <summary>
/// The caller's ids, read from the validated token and nowhere else (api-design overlay): an endpoint never takes
/// a merchant id from the path, query, or body.
/// </summary>
public static class CallerClaims
{
    public const string MerchantId = "merchant_id";

    /// <summary>On a staff token only, as <see cref="StaffRole"/> (ADR 011); a merchant user's token has no role.</summary>
    public const string Role = "role";

    public const string StaffRole = "staff";

    /// <summary>
    /// True for a staff token: the staff role, a well-formed subject, and no merchant id; the <c>staff</c> policy
    /// requires it.
    /// </summary>
    public static bool IsStaff(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return principal.HasClaim(Role, StaffRole)
            && Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out _)
            && principal.FindFirstValue(MerchantId) is null;
    }

    public static StaffUserId GetStaffId(this ClaimsPrincipal principal) =>
        new(ParseRequired(principal, JwtRegisteredClaimNames.Sub));

    /// <summary>True when both ids are present and well formed; the default authorization policy requires it.</summary>
    public static bool HasCallerIds(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.FindFirstValue(JwtRegisteredClaimNames.Sub), out _)
            && Guid.TryParse(principal.FindFirstValue(MerchantId), out _);
    }

    public static MerchantUserId GetUserId(this ClaimsPrincipal principal) =>
        new(ParseRequired(principal, JwtRegisteredClaimNames.Sub));

    public static MerchantId GetMerchantId(this ClaimsPrincipal principal) =>
        new(ParseRequired(principal, MerchantId));

    // Only reachable behind the default policy, which has already checked both claims parse.
    private static Guid ParseRequired(ClaimsPrincipal principal, string type)
    {
        ArgumentNullException.ThrowIfNull(principal);
        return Guid.TryParse(principal.FindFirstValue(type), out var value)
            ? value
            : throw new InvalidOperationException($"the '{type}' claim is missing; is the endpoint behind the default policy?");
    }
}
