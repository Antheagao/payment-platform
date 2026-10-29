using System.Globalization;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PaymentPlatform.Api.Errors;

namespace PaymentPlatform.Api.Auth;

/// <summary>
/// Bearer-token authentication, the default authorization policy, and the login rate limits (ADR 004).
/// </summary>
public static partial class AuthenticationSetup
{
    public const string LoginPerAddressPolicy = "login-per-address";

    /// <summary>
    /// Registers everything a protected endpoint and the login endpoint need. Returns true when no signing key is
    /// configured and the environment is Development, in which case a random key is generated for this process:
    /// the stack starts with no setup and no committed secret, and logins end when the API restarts.
    /// </summary>
    public static bool AddApiAuthentication(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var services = builder.Services;

        var ephemeralKey = string.IsNullOrWhiteSpace(builder.Configuration[$"{JwtOptions.Section}:SigningKey"])
            && builder.Environment.IsDevelopment()
                ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(JwtOptions.MinimumKeyBytes))
                : null;
        services.AddOptions<JwtOptions>()
            .BindConfiguration(JwtOptions.Section)
            .PostConfigure(options =>
            {
                if (ephemeralKey is not null)
                {
                    options.SigningKey = ephemeralKey;
                }
            })
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<JwtOptions>, JwtOptions.Validator>();
        services.AddSingleton<TokenIssuer>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) => ConfigureBearer(bearer, jwtOptions.Value));

        // Every endpoint under /api/v1 requires this; the login endpoint opts out with AllowAnonymous.
        services.AddAuthorizationBuilder().SetDefaultPolicy(
            new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireAssertion(context => CallerClaims.HasCallerIds(context.User))
                .Build());

        services.AddOptions<LoginRateLimitOptions>()
            .BindConfiguration(LoginRateLimitOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<LoginThrottle>();
        services.AddOptions<DashboardOptions>()
            .BindConfiguration(DashboardOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                // The body is the rate_limited problem that status code pages writes for an empty 429.
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = RetryAfterSeconds(retryAfter);
                }

                return ValueTask.CompletedTask;
            };
            options.AddPolicy(LoginPerAddressPolicy, PartitionByAddress);
        });

        return ephemeralKey is not null;
    }

    /// <summary>A <c>Retry-After</c> value: whole seconds, rounded up, and never zero.</summary>
    public static string RetryAfterSeconds(TimeSpan wait) =>
        Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)).ToString(CultureInfo.InvariantCulture);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Jwt:SigningKey is not set, so tokens are signed with a key generated at startup (Development only); logins end when the API restarts")]
    public static partial void LogEphemeralSigningKey(ILogger logger);

    private static void ConfigureBearer(JwtBearerOptions bearer, JwtOptions jwt)
    {
        // Claims keep their JWT names (sub, merchant_id) instead of being mapped to WS-Federation URIs.
        bearer.MapInboundClaims = false;
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(jwt.SigningKeyBytes()),
            // Pinned, so a token cannot choose its own algorithm (alg "none", or HS512 with the same key).
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
        bearer.Events = new JwtBearerEvents { OnChallenge = WriteChallengeAsync };
    }

    /// <summary>
    /// Answers a missing or rejected token with the API's problem shape, and tells an expired token apart
    /// (<c>token_expired</c>) so the dashboard can send the user back to log in.
    /// </summary>
    private static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        var expired = context.AuthenticateFailure is SecurityTokenExpiredException;
        var response = context.Response;
        response.StatusCode = StatusCodes.Status401Unauthorized;
        // RFC 6750: a request with no token gets the bare challenge, a rejected token says so.
        response.Headers.WWWAuthenticate = context.AuthenticateFailure is null ? "Bearer" : "Bearer error=\"invalid_token\"";

        var problems = context.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = context.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Detail = expired ? "the access token has expired; log in again" : "a valid access token is required",
                Extensions = { ["code"] = expired ? ProblemCodes.TokenExpired : ProblemCodes.Unauthorized },
            },
        });
    }

    private static RateLimitPartition<string> PartitionByAddress(HttpContext context)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<LoginRateLimitOptions>>().Value;
        return RateLimitPartition.GetFixedWindowLimiter(ClientAddress.PartitionOf(context), _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = limits.PerAddressPermits,
            Window = limits.PerAddressWindow,
            QueueLimit = 0,
        });
    }
}
