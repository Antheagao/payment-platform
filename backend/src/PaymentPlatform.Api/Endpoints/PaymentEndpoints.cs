using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Options;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.Api.Errors;
using PaymentPlatform.Application.Idempotency;
using PaymentPlatform.Application.Payments;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.ServiceDefaults;

namespace PaymentPlatform.Api.Endpoints;

/// <summary><c>POST /api/v1/payments</c> and <c>GET /api/v1/payments/{id}</c>, scoped to the caller's merchant.</summary>
public static class PaymentEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    /// <summary>Set to <c>true</c> on a response that is a stored answer to an earlier request with the same key.</summary>
    public const string ReplayedHeader = "Idempotent-Replayed";

    public static RouteGroupBuilder MapPaymentEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapPost("/payments", CreateAsync)
            .WithName("CreatePayment")
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status409Conflict)
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

    /// <summary>
    /// Creates a payment under the caller's <c>Idempotency-Key</c> (ADR 007). Invalid requests are answered before
    /// the key is claimed, so nothing is stored for them and a corrected request may use the same key; once work
    /// starts, the response is stored and every retry with the same request gets it back.
    /// </summary>
    private static async Task<Results<ContentHttpResult, ValidationProblem, ProblemHttpResult>> CreateAsync(
        CreatePaymentRequest request,
        ClaimsPrincipal caller,
        CreatePayment createPayment,
        IdempotencyStore idempotency,
        TimeProvider timeProvider,
        IOptions<JsonOptions> json,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        var key = http.Request.Headers[IdempotencyKeyHeader].ToString();
        if (key.Length == 0)
        {
            return ApiProblems.Create(
                StatusCodes.Status400BadRequest, "idempotency_key_required", "send an Idempotency-Key header that is unique to this payment");
        }

        if (!IsAcceptableKey(key))
        {
            return ApiProblems.Create(
                StatusCodes.Status400BadRequest, "idempotency_key_invalid", "an Idempotency-Key is 1 to 255 printable ASCII characters");
        }

        var errors = request.Validate(timeProvider.GetUtcNow());
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var claim = await idempotency.ClaimAsync(caller.GetMerchantId(), key, request.Fingerprint(), cancellationToken);
        switch (claim.Outcome)
        {
            case ClaimOutcome.Replay:
                http.Response.Headers[ReplayedHeader] = "true";
                return Send(claim.Response!, http);
            case ClaimOutcome.InProgress:
                return ApiProblems.Create(
                    StatusCodes.Status409Conflict,
                    "idempotency_request_in_progress",
                    "a request with this Idempotency-Key is still running; retry shortly");
            case ClaimOutcome.Mismatch:
                return ApiProblems.Create(
                    StatusCodes.Status422UnprocessableEntity,
                    "idempotency_key_reused",
                    "this Idempotency-Key was used for a different request");
        }

        try
        {
            var result = await createPayment.HandleAsync(
                request, new PaymentCaller(caller.GetMerchantId(), caller.GetUserId(), http.GetCorrelationId()), claim, cancellationToken);
            if (!result.IsSuccess)
            {
                // Nothing was created, so nothing is stored: the same request gets the same answer, and a
                // corrected one may use the key.
                await idempotency.ReleaseAsync(claim);
                return ApiProblems.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Code, result.Error.Message);
            }

            var body = PaymentResponse.From(result.Value);
            var response = new StoredResponse(
                StatusCodes.Status201Created,
                JsonSerializer.Serialize(body, json.Value.SerializerOptions),
                $"{ApiRoutes.V1}/payments/{body.Id}");
            await idempotency.CompleteAsync(claim, response, cancellationToken);
            return Send(response, http);
        }
        catch
        {
            // A failure lets the client retry: the key is freed, or, if a payment exists, left for the retry to finish.
            await idempotency.ReleaseAsync(claim);
            throw;
        }
    }

    /// <summary>1 to 255 printable ASCII characters (ADR 007); anything else could not be stored or compared exactly.</summary>
    private static bool IsAcceptableKey(string key) =>
        key.Length is >= 1 and <= 255 && key.All(character => character is >= ' ' and <= '~') && key.Trim().Length > 0;

    /// <summary>The same bytes for the first answer and every replay of it.</summary>
    private static ContentHttpResult Send(StoredResponse response, HttpContext http)
    {
        if (response.Location is not null)
        {
            http.Response.Headers.Location = response.Location;
        }

        return TypedResults.Text(response.Body, "application/json", Encoding.UTF8, response.Status);
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
