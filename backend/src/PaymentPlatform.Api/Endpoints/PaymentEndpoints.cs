using System.Security.Claims;
using System.Security.Cryptography;
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
using FromQueryAttribute = Microsoft.AspNetCore.Mvc.FromQueryAttribute;

namespace PaymentPlatform.Api.Endpoints;

/// <summary>
/// <c>POST /api/v1/payments</c>, <c>GET /api/v1/payments/{id}</c>, and <c>POST /api/v1/payments/{id}/capture</c> and
/// <c>/refund</c>, scoped to the caller's merchant; every money-moving call takes an <c>Idempotency-Key</c>.
/// </summary>
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
        group.MapGet("/payments", ListAsync)
            .WithName("ListPayments")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapGet("/payments/{id:guid}", GetAsync)
            .WithName("GetPayment")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        MapMove(group.MapPost("/payments/{id:guid}/capture", CaptureAsync).WithName("CapturePayment"));
        MapMove(group.MapPost("/payments/{id:guid}/refund", RefundAsync).WithName("RefundPayment"));
        return group;
    }

    private static void MapMove(RouteHandlerBuilder endpoint)
    {
        endpoint
            .Produces<PaymentResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
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

    /// <summary>
    /// The list's query string. Every value binds as text and is parsed by <see cref="ListPaymentsRequest"/>, so a
    /// bad one is a field code rather than the binder's bare 400; <c>status</c> may repeat or hold a comma list.
    /// </summary>
    public sealed record ListPaymentsParameters(
        [property: FromQuery(Name = "limit")] string? Limit,
        [property: FromQuery(Name = "cursor")] string? Cursor,
        [property: FromQuery(Name = "status")] string[]? Status,
        [property: FromQuery(Name = "created_from")] string? CreatedFrom,
        [property: FromQuery(Name = "created_to")] string? CreatedTo,
        [property: FromQuery(Name = "amount_min_minor")] string? AmountMinMinor,
        [property: FromQuery(Name = "amount_max_minor")] string? AmountMaxMinor,
        [property: FromQuery(Name = "currency")] string? Currency,
        [property: FromQuery(Name = "reference")] string? Reference);

    /// <summary>One page, newest first; <c>next_cursor</c> is null on the last page.</summary>
    public sealed record PaymentListResponse(IReadOnlyList<PaymentSummaryResponse> Data, string? NextCursor);

    public sealed record PaymentSummaryResponse(
        Guid Id,
        PaymentStatus Status,
        long AmountMinor,
        string Currency,
        string? MerchantReference,
        CardSummaryResponse Card,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record CardSummaryResponse(CardBrand Brand, string Last4);

    public sealed record PaymentHistoryResponse(
        PaymentStatus? From, PaymentStatus To, ActorResponse Actor, DateTimeOffset OccurredAt, string? Reason, string? CorrelationId);

    public sealed record ActorResponse(ActorKind Kind, string Id);

    /// <summary>
    /// Creates a payment under the caller's <c>Idempotency-Key</c> (ADR 007). Invalid requests are answered before
    /// the key is claimed, so nothing is stored for them and a corrected request may use the same key; once a
    /// payment exists, the response is stored and every retry with the same request gets it back.
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
        if (ReadIdempotencyKey(http, out var key) is { } keyProblem)
        {
            return keyProblem;
        }

        var errors = request.Validate(timeProvider.GetUtcNow());
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var (sent, problem) = await RunIdempotentAsync(
            http,
            idempotency,
            caller,
            key,
            request.Fingerprint(),
            claim => createPayment.HandleAsync(request, CallerOf(caller, http), claim, cancellationToken),
            StatusCodes.Status201Created,
            json.Value.SerializerOptions,
            cancellationToken);
        return sent is not null ? sent : problem!;
    }

    private static Task<Results<ContentHttpResult, ProblemHttpResult>> CaptureAsync(
        Guid id,
        ClaimsPrincipal caller,
        CapturePayment capture,
        IdempotencyStore idempotency,
        IOptions<JsonOptions> json,
        HttpContext http,
        CancellationToken cancellationToken) =>
        MoveAsync("capture", id, caller, capture, idempotency, json, http, cancellationToken);

    private static Task<Results<ContentHttpResult, ProblemHttpResult>> RefundAsync(
        Guid id,
        ClaimsPrincipal caller,
        RefundPayment refund,
        IdempotencyStore idempotency,
        IOptions<JsonOptions> json,
        HttpContext http,
        CancellationToken cancellationToken) =>
        MoveAsync("refund", id, caller, refund, idempotency, json, http, cancellationToken);

    /// <summary>
    /// Capture or refund under an <c>Idempotency-Key</c>: a retry with the same key replays the first success; a
    /// new key on a payment already moved is the lifecycle's <c>409</c>, the second line of defense (ADR 007).
    /// </summary>
    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> MoveAsync(
        string operation,
        Guid id,
        ClaimsPrincipal caller,
        MovePayment move,
        IdempotencyStore idempotency,
        IOptions<JsonOptions> json,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        if (ReadIdempotencyKey(http, out var key) is { } keyProblem)
        {
            return keyProblem;
        }

        // The operation and the payment are the whole request, so they are the whole fingerprint.
        var fingerprint = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new[] { $"POST {ApiRoutes.V1}/payments/{id}/{operation}" }));
        var (sent, problem) = await RunIdempotentAsync(
            http,
            idempotency,
            caller,
            key,
            fingerprint,
            _ => move.HandleAsync(new PaymentId(id), CallerOf(caller, http), cancellationToken),
            StatusCodes.Status200OK,
            json.Value.SerializerOptions,
            cancellationToken);
        return sent is not null ? sent : problem!;
    }

    /// <summary>
    /// Claims the key, runs the work, and stores and sends a success; anything else releases the key, because
    /// nothing changed and the same request may be tried again.
    /// </summary>
    private static async Task<(ContentHttpResult? Sent, ProblemHttpResult? Problem)> RunIdempotentAsync(
        HttpContext http,
        IdempotencyStore idempotency,
        ClaimsPrincipal caller,
        string key,
        byte[] fingerprint,
        Func<IdempotencyClaim, Task<Result<PaymentDetails>>> work,
        int successStatus,
        JsonSerializerOptions json,
        CancellationToken cancellationToken)
    {
        var claim = await idempotency.ClaimAsync(caller.GetMerchantId(), key, fingerprint, cancellationToken);
        switch (claim.Outcome)
        {
            case ClaimOutcome.Replay:
                http.Response.Headers[ReplayedHeader] = "true";
                return (Send(claim.Response!, http), null);
            case ClaimOutcome.InProgress:
                return (null, ApiProblems.Create(
                    StatusCodes.Status409Conflict,
                    "idempotency_request_in_progress",
                    "a request with this Idempotency-Key is still running; retry shortly"));
            case ClaimOutcome.Mismatch:
                return (null, ApiProblems.Create(
                    StatusCodes.Status422UnprocessableEntity,
                    "idempotency_key_reused",
                    "this Idempotency-Key was used for a different request"));
        }

        try
        {
            var result = await work(claim);
            if (!result.IsSuccess)
            {
                await idempotency.ReleaseAsync(claim);
                return (null, ProblemFor(result.Error, http));
            }

            var body = PaymentResponse.From(result.Value);
            var response = new StoredResponse(
                successStatus,
                JsonSerializer.Serialize(body, json),
                successStatus == StatusCodes.Status201Created ? $"{ApiRoutes.V1}/payments/{body.Id}" : null);
            await idempotency.CompleteAsync(claim, response, cancellationToken);
            return (Send(response, http), null);
        }
        catch
        {
            // A failure lets the client retry: the key is freed, or, if a payment exists, left for the retry to finish.
            await idempotency.ReleaseAsync(claim);
            throw;
        }
    }

    /// <summary>The status for each error a payment use case returns; its code goes into the body unchanged.</summary>
    private static ProblemHttpResult ProblemFor(DomainError error, HttpContext http)
    {
        var status = error.Code switch
        {
            "not_found" => StatusCodes.Status404NotFound,
            "acquirer_unavailable" => StatusCodes.Status503ServiceUnavailable,
            _ when error.Code.StartsWith("payment_not_", StringComparison.Ordinal) => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status422UnprocessableEntity,
        };
        if (status == StatusCodes.Status503ServiceUnavailable)
        {
            http.Response.Headers.RetryAfter = "5";
        }

        return ApiProblems.Create(status, error.Code, error.Message);
    }

    private static PaymentCaller CallerOf(ClaimsPrincipal caller, HttpContext http) =>
        new(caller.GetMerchantId(), caller.GetUserId(), http.GetCorrelationId());

    /// <summary>Reads the <c>Idempotency-Key</c> header; a problem when it is missing or not 1 to 255 printable ASCII characters.</summary>
    private static ProblemHttpResult? ReadIdempotencyKey(HttpContext http, out string key)
    {
        key = http.Request.Headers[IdempotencyKeyHeader].ToString();
        if (key.Length == 0)
        {
            return ApiProblems.Create(
                StatusCodes.Status400BadRequest, "idempotency_key_required", "send an Idempotency-Key header that is unique to this request");
        }

        return IsAcceptableKey(key)
            ? null
            : ApiProblems.Create(
                StatusCodes.Status400BadRequest, "idempotency_key_invalid", "an Idempotency-Key is 1 to 255 printable ASCII characters");
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

    private static async Task<Results<Ok<PaymentListResponse>, ValidationProblem>> ListAsync(
        [AsParameters] ListPaymentsParameters parameters,
        ClaimsPrincipal caller,
        ListPayments listPayments,
        CancellationToken cancellationToken)
    {
        var request = new ListPaymentsRequest(
            parameters.Limit,
            parameters.Cursor,
            parameters.Status,
            parameters.CreatedFrom,
            parameters.CreatedTo,
            parameters.AmountMinMinor,
            parameters.AmountMaxMinor,
            parameters.Currency,
            parameters.Reference);
        var (query, errors) = request.Parse();
        if (query is null)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var page = await listPayments.HandleAsync(caller.GetMerchantId(), query, cancellationToken);
        return TypedResults.Ok(new PaymentListResponse(
            page.Data
                .Select(payment => new PaymentSummaryResponse(
                    payment.Id.Value,
                    payment.Status,
                    payment.AmountMinor,
                    payment.Currency,
                    payment.MerchantReference,
                    new CardSummaryResponse(payment.CardBrand, payment.CardLast4),
                    payment.CreatedAt,
                    payment.UpdatedAt))
                .ToList(),
            page.NextCursor));
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
