using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.Api.Errors;
using PaymentPlatform.Application.Auth;

namespace PaymentPlatform.Api.Endpoints;

/// <summary><c>POST /api/v1/auth/login</c> and <c>GET /api/v1/me</c> (ADR 004).</summary>
public static class AuthEndpoints
{
    public const string TokenType = "Bearer";

    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/auth/login", LogInAsync)
            .WithName("LogIn")
            .AllowAnonymous()
            .RequireRateLimiting(AuthenticationSetup.LoginPerAddressPolicy)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapGet("/me", GetCurrentUserAsync)
            .WithName("GetCurrentUser")
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        return group;
    }

    /// <param name="AccessToken">Sent back as <c>Authorization: Bearer ...</c> on every other call.</param>
    /// <param name="ExpiresAt">When the token stops being accepted; there is no refresh, so log in again.</param>
    /// <param name="ExpiresIn">The same instant as seconds from now, as OAuth clients expect.</param>
    public sealed record LoginResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt, int ExpiresIn);

    public sealed record CurrentUserResponse(Guid UserId, Guid MerchantId, string Email, string MerchantName);

    private static async Task<Results<Ok<LoginResponse>, ValidationProblem, ProblemHttpResult>> LogInAsync(
        LogInRequest request,
        LogIn logIn,
        LoginThrottle throttle,
        TokenIssuer tokens,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var errors = request.Validate();
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!throttle.TryAcquire(request.Email!, out var retryAfter))
        {
            http.Response.Headers.RetryAfter = AuthenticationSetup.RetryAfterSeconds(retryAfter);
            return ApiProblems.Create(
                StatusCodes.Status429TooManyRequests,
                ProblemCodes.RateLimited,
                "too many login attempts for this account; try again after Retry-After seconds");
        }

        var result = await logIn.HandleAsync(request, cancellationToken);
        if (!result.IsSuccess)
        {
            http.Response.Headers.WWWAuthenticate = TokenType;
            return ApiProblems.Create(StatusCodes.Status401Unauthorized, result.Error.Code, result.Error.Message);
        }

        var token = tokens.Issue(result.Value);
        return TypedResults.Ok(new LoginResponse(token.AccessToken, TokenType, token.ExpiresAt, token.ExpiresInSeconds));
    }

    private static async Task<Results<Ok<CurrentUserResponse>, ProblemHttpResult>> GetCurrentUserAsync(
        ClaimsPrincipal caller,
        GetCurrentUser query,
        CancellationToken cancellationToken)
    {
        var user = await query.HandleAsync(caller.GetUserId(), caller.GetMerchantId(), cancellationToken);
        return user is null
            ? ApiProblems.Create(StatusCodes.Status401Unauthorized, ProblemCodes.Unauthorized, "the user this token names no longer exists")
            : TypedResults.Ok(new CurrentUserResponse(user.UserId.Value, user.MerchantId.Value, user.Email, user.MerchantName));
    }
}
