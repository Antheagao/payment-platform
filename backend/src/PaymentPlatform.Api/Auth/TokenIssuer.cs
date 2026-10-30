using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Application.Staff;

namespace PaymentPlatform.Api.Auth;

/// <summary>An access token, the instant it stops being accepted, and its lifetime in seconds.</summary>
public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt, int ExpiresInSeconds);

/// <summary>
/// Signs access tokens with HS256 (ADR 004): <c>sub</c> is the user, <c>merchant_id</c> the merchant every request
/// is scoped to, <c>jti</c> a unique id, and <c>iat</c>, <c>nbf</c>, and <c>exp</c> come from the injected clock.
/// </summary>
public sealed class TokenIssuer(IOptions<JwtOptions> options, TimeProvider timeProvider)
{
    private readonly JsonWebTokenHandler _handler = new() { SetDefaultTimesOnTokenCreation = false };

    public IssuedToken Issue(LoggedInUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return Issue(new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.UserId.ToString(),
            [CallerClaims.MerchantId] = user.MerchantId.ToString(),
        });
    }

    /// <summary>A staff token (ADR 011): the staff user in <c>sub</c>, the <c>staff</c> role, and no merchant id.</summary>
    public IssuedToken Issue(LoggedInStaff staff)
    {
        ArgumentNullException.ThrowIfNull(staff);
        return Issue(new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = staff.StaffId.ToString(),
            [CallerClaims.Role] = CallerClaims.StaffRole,
        });
    }

    private IssuedToken Issue(Dictionary<string, object> claims)
    {
        var jwt = options.Value;
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(jwt.LifetimeMinutes);
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>(claims)
            {
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(jwt.SigningKeyBytes()), SecurityAlgorithms.HmacSha256),
        });
        return new IssuedToken(token, expiresAt, jwt.LifetimeMinutes * 60);
    }
}
