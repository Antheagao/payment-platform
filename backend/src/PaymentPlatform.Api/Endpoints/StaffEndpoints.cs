using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.Api.Errors;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Application.Staff;
using PaymentPlatform.Domain;
using PaymentPlatform.Persistence;
using FromQueryAttribute = Microsoft.AspNetCore.Mvc.FromQueryAttribute;

namespace PaymentPlatform.Api.Endpoints;

/// <summary>
/// The staff API under <c>/api/v1/staff</c> (ADR 011): sign-in, who is signed in, and the dead-lettered webhook
/// deliveries with their replay. Every route but the sign-in needs the <c>staff</c> policy.
/// </summary>
public static class StaffEndpoints
{
    public static RouteGroupBuilder MapStaffEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/auth/login", LogInAsync)
            .WithName("StaffLogIn")
            .AllowAnonymous()
            .RequireRateLimiting(AuthenticationSetup.LoginPerAddressPolicy)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);
        group.MapGet("/me", GetCurrentStaffAsync)
            .WithName("GetCurrentStaff")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapGet("/webhook-deliveries", ListDeliveriesAsync)
            .WithName("ListWebhookDeliveries")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);
        group.MapPost("/webhook-deliveries/{id:long}/replay", ReplayAsync)
            .WithName("ReplayWebhookDelivery")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
        return group;
    }

    public sealed record CurrentStaffResponse(Guid StaffId, string Email);

    /// <summary>The list's query string, bound as text and checked by <see cref="ListDeadLetteredDeliveriesRequest"/>.</summary>
    public sealed record ListDeliveriesParameters(
        [property: FromQuery(Name = "status")] string? Status,
        [property: FromQuery(Name = "limit")] string? Limit,
        [property: FromQuery(Name = "cursor")] string? Cursor);

    public sealed record DeliveryListResponse(IReadOnlyList<DeadLetteredDeliveryResponse> Data, string? NextCursor);

    public sealed record DeadLetteredDeliveryResponse(
        long Id,
        Guid MerchantId,
        Guid PaymentId,
        string EventType,
        int Attempts,
        int? LastStatusCode,
        string? LastError,
        string EndpointHost,
        DateTimeOffset CreatedAt);

    /// <summary>A delivery as a replay left it: back on the queue, due at <c>next_attempt_at</c>.</summary>
    public sealed record ReplayedDeliveryResponse(long Id, WebhookDeliveryStatus Status, int Attempts, DateTimeOffset NextAttemptAt);

    // The same shape and limits as the merchant login (AuthEndpoints), against the staff users.
    private static async Task<Results<Ok<AuthEndpoints.LoginResponse>, ValidationProblem, ProblemHttpResult>> LogInAsync(
        LogInRequest request,
        LogInStaff logIn,
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

        // Its own namespace in the per-email limit, so a merchant's attempts never spend a staff user's.
        if (!throttle.TryAcquire($"staff:{request.Email}", out var retryAfter))
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
            http.Response.Headers.WWWAuthenticate = AuthEndpoints.TokenType;
            return ApiProblems.Create(StatusCodes.Status401Unauthorized, result.Error.Code, result.Error.Message);
        }

        var token = tokens.Issue(result.Value);
        return TypedResults.Ok(new AuthEndpoints.LoginResponse(token.AccessToken, AuthEndpoints.TokenType, token.ExpiresAt, token.ExpiresInSeconds));
    }

    private static async Task<Results<Ok<CurrentStaffResponse>, ProblemHttpResult>> GetCurrentStaffAsync(
        ClaimsPrincipal caller,
        GetCurrentStaff query,
        CancellationToken cancellationToken)
    {
        var staff = await query.HandleAsync(caller.GetStaffId(), cancellationToken);
        return staff is null
            ? ApiProblems.Create(StatusCodes.Status401Unauthorized, ProblemCodes.Unauthorized, "the staff user this token names no longer exists")
            : TypedResults.Ok(new CurrentStaffResponse(staff.StaffId.Value, staff.Email));
    }

    private static async Task<Results<Ok<DeliveryListResponse>, ValidationProblem>> ListDeliveriesAsync(
        [AsParameters] ListDeliveriesParameters parameters,
        ListDeadLetteredDeliveries list,
        CancellationToken cancellationToken)
    {
        var (limit, before, errors) = new ListDeadLetteredDeliveriesRequest(parameters.Status, parameters.Limit, parameters.Cursor).Parse();
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var page = await list.HandleAsync(limit, before, cancellationToken);
        return TypedResults.Ok(new DeliveryListResponse(
            page.Data.Select(delivery => new DeadLetteredDeliveryResponse(
                delivery.Id,
                delivery.MerchantId.Value,
                delivery.PaymentId.Value,
                delivery.EventType,
                delivery.Attempts,
                delivery.LastStatusCode,
                delivery.LastError,
                delivery.EndpointHost,
                delivery.CreatedAt)).ToList(),
            page.NextCursor));
    }

    private static async Task<Results<Ok<ReplayedDeliveryResponse>, ProblemHttpResult>> ReplayAsync(
        long id,
        ClaimsPrincipal caller,
        ReplayDeadLetteredDelivery replay,
        CancellationToken cancellationToken)
    {
        var result = await replay.HandleAsync(id, caller.GetStaffId(), cancellationToken);
        if (!result.IsSuccess)
        {
            return ProblemFor(result.Error);
        }

        var delivery = result.Value;
        return TypedResults.Ok(new ReplayedDeliveryResponse(delivery.Id, delivery.Status, delivery.Attempts, delivery.NextAttemptAt));
    }

    private static ProblemHttpResult ProblemFor(DomainError error) =>
        ApiProblems.Create(
            error == ReplayDeadLetteredDelivery.NotFound ? StatusCodes.Status404NotFound : StatusCodes.Status409Conflict,
            error.Code,
            error.Message);
}
