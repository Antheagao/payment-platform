using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;

namespace PaymentPlatform.Api.Auth;

/// <summary>
/// How the API signs and checks access tokens (ADR 004), from the <c>Jwt</c> section. The signing key is a
/// secret: Key Vault in Azure (<c>Jwt__SigningKey</c>, infra/), and never a committed default.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";

    /// <summary>HMAC-SHA256 needs a key at least as long as its output.</summary>
    public const int MinimumKeyBytes = 32;

    [Required]
    public string Issuer { get; set; } = "payment-platform-api";

    [Required]
    public string Audience { get; set; } = "payment-platform";

    /// <summary>Base64, at least <see cref="MinimumKeyBytes"/> bytes once decoded.</summary>
    public string? SigningKey { get; set; }

    /// <summary>Absolute, with no refresh token (ADR 004, assumption 15).</summary>
    [Range(1, 60)]
    public int LifetimeMinutes { get; set; } = 30;

    /// <summary>The decoded key; only valid once <see cref="Validator"/> has passed.</summary>
    public byte[] SigningKeyBytes() => Convert.FromBase64String(SigningKey ?? string.Empty);

    /// <summary>Refuses a missing, malformed, or short key at startup rather than on the first login.</summary>
    internal sealed class Validator : IValidateOptions<JwtOptions>
    {
        public ValidateOptionsResult Validate(string? name, JwtOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.SigningKey))
            {
                return ValidateOptionsResult.Fail(
                    "Jwt:SigningKey is not set: give it a base64 key of at least 32 random bytes (Key Vault in Azure).");
            }

            var buffer = new byte[options.SigningKey.Length];
            if (!Convert.TryFromBase64String(options.SigningKey, buffer, out var length))
            {
                return ValidateOptionsResult.Fail("Jwt:SigningKey is not valid base64.");
            }

            return length < MinimumKeyBytes
                ? ValidateOptionsResult.Fail($"Jwt:SigningKey decodes to {length} bytes; HS256 needs at least {MinimumKeyBytes}.")
                : ValidateOptionsResult.Success;
        }
    }
}
