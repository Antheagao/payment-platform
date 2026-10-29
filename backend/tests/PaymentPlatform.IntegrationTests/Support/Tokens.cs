using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>Hand-made tokens, for the ways a caller might try to get past the API's validation.</summary>
public static class Tokens
{
    public const string Issuer = "payment-platform-api";
    public const string Audience = "payment-platform";

    /// <summary>
    /// A token shaped like the API's own. Every part can be changed: claims, times, the key, the algorithm, the
    /// issuer, and the audience.
    /// </summary>
    public static string Forge(
        IDictionary<string, object> claims,
        DateTimeOffset? issuedAt = null,
        TimeSpan? lifetime = null,
        byte[]? key = null,
        string algorithm = SecurityAlgorithms.HmacSha256,
        string issuer = Issuer,
        string audience = Audience)
    {
        var start = issuedAt ?? TimeProvider.System.GetUtcNow();
        var handler = new JsonWebTokenHandler { SetDefaultTimesOnTokenCreation = false };
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = start.UtcDateTime,
            NotBefore = start.UtcDateTime,
            Expires = (start + (lifetime ?? TimeSpan.FromMinutes(30))).UtcDateTime,
            Claims = claims,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key ?? ApiFactory.SigningKey), algorithm),
        });
    }

    /// <summary>The claims the API puts in a token for <paramref name="user"/>.</summary>
    public static Dictionary<string, object> ClaimsFor(TestUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.UserId.ToString(),
            ["merchant_id"] = user.MerchantId.ToString(),
            [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
        };
    }

    /// <summary>An unsigned token (<c>alg: none</c>) with the given claims, which no library will mint on request.</summary>
    public static string WithoutSignature(IDictionary<string, object> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        var now = TimeProvider.System.GetUtcNow();
        var payload = new Dictionary<string, object>(claims)
        {
            ["iss"] = Issuer,
            ["aud"] = Audience,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["nbf"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(30).ToUnixTimeSeconds(),
        };
        return $"{Base64UrlEncoder.Encode("""{"alg":"none","typ":"JWT"}""")}.{Base64UrlEncoder.Encode(System.Text.Json.JsonSerializer.Serialize(payload))}.";
    }
}
