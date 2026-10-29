using System.Security.Claims;
using Microsoft.AspNetCore.Http.HttpResults;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.Api.Errors;
using PaymentPlatform.Application.Payments;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.ServiceDefaults;

namespace PaymentPlatform.Api.Endpoints;

/// <summary><c>POST /api/v1/payments</c> and <c>GET /api/v1/payments/{id}</c>, scoped to the caller's merchant.</summary>
public static class PaymentEndpoints
{
    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/payments", CreateAsync)
            .WithName("CreatePayment")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        group.MapGet("/payments/{id:guid}", GetAsync)
            .WithName("GetPayment")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    /// <summary>
    /// A payment as the API returns it: the card's brand, last four digits, and expiry, never its token or number;
    /// and its history, oldest first.
    /// </summary>
    public sealed record PaymentResponse(
        Guid Id,
        PaymentStatus Status,
        long AmountMinor,
        string Currency,
        string? MerchantReference,
        CardResponse Card,
        string? AuthorizationReference,
        string? FailureReason,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        IReadOnlyList<PaymentHistoryResponse> History)
    {
        public static PaymentResponse From(PaymentDetails payment)
        {
            ArgumentNullException.ThrowIfNull(payment);
            return new PaymentResponse(
                payment.Id.Value,
                payment.Status,
                payment.AmountMinor,
                payment.Currency,
                payment.MerchantReference,
                new CardResponse(payment.CardBrand, payment.CardLast4, payment.CardExpiryMonth, payment.CardExpiryYear),
                payment.AuthorizationReference,
                payment.FailureReason,
                payment.CreatedAt,
                payment.UpdatedAt,
                payment.History
                    .Select(entry => new PaymentHistoryResponse(
                        entry.From,
                        entry.To,
                        new ActorResponse(entry.ActorKind, entry.ActorId),
                        entry.OccurredAt,
                        entry.Reason,
                        entry.CorrelationId))
                    .ToList());
        }
    }

    public sealed record CardResponse(CardBrand Brand, string Last4, int ExpMonth, int ExpYear);

    public sealed record PaymentHistoryResponse(
        PaymentStatus? From, PaymentStatus To, ActorResponse Actor, DateTimeOffset OccurredAt, string? Reason, string? CorrelationId);

    public sealed record ActorResponse(ActorKind Kind, string Id);

    private static async Task<Results<Created<PaymentResponse>, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreatePaymentRequest request,
        ClaimsPrincipal caller,
        CreatePayment createPayment,
        TimeProvider timeProvider,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var errors = request.Validate(timeProvider.GetUtcNow());
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var result = await createPayment.HandleAsync(
            request, new PaymentCaller(caller.GetMerchantId(), caller.GetUserId(), http.GetCorrelationId()), cancellationToken);
        if (!result.IsSuccess)
        {
            return ApiProblems.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Code, result.Error.Message);
        }

        var body = PaymentResponse.From(result.Value);
        return TypedResults.Created($"{ApiRoutes.V1}/payments/{body.Id}", body);
    }

    private static async Task<Results<Ok<PaymentResponse>, ProblemHttpResult>> GetAsync(
        Guid id,
        ClaimsPrincipal caller,
        GetPayment getPayment,
        CancellationToken cancellationToken)
    {
        var payment = await getPayment.HandleAsync(new PaymentId(id), caller.GetMerchantId(), cancellationToken);
        return payment is null
            ? ApiProblems.Create(StatusCodes.Status404NotFound, ProblemCodes.NotFound, "no payment with this id")
            : TypedResults.Ok(PaymentResponse.From(payment));
    }
}
