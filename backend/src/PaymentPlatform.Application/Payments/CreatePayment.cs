using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Idempotency;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Payments;

/// <summary>A new payment as the API received it; any field may be missing, which <see cref="Validate"/> reports.</summary>
public sealed record CreatePaymentRequest(long? AmountMinor, string? Currency, string? MerchantReference, CardInput? Card)
{
    /// <summary>Field errors as codes, by snake_case field name; empty when the payment can be attempted.</summary>
    public IReadOnlyDictionary<string, string[]> Validate(DateTimeOffset now)
    {
        var errors = new Dictionary<string, string[]>();
        if (AmountMinor is null)
        {
            errors["amount_minor"] = ["amount_required"];
        }
        else if (AmountMinor <= 0)
        {
            errors["amount_minor"] = ["amount_must_be_positive"];
        }
        else if (AmountMinor > Money.MaxAmountMinor)
        {
            errors["amount_minor"] = ["amount_too_large"];
        }

        if (string.IsNullOrWhiteSpace(Currency))
        {
            errors["currency"] = ["currency_required"];
        }
        else if (Domain.Monetary.Currency.Parse(Currency) is { IsSuccess: false } currency)
        {
            errors["currency"] = [currency.Error.Code];
        }

        if (MerchantReference is not null
            && (string.IsNullOrWhiteSpace(MerchantReference) || MerchantReference.Trim().Length > Payment.MerchantReferenceMaxLength))
        {
            errors["merchant_reference"] = ["merchant_reference_invalid"];
        }

        if (Card is null)
        {
            errors["card"] = ["card_required"];
        }
        else
        {
            foreach (var (field, codes) in Card.Validate("card", now))
            {
                errors[field] = codes;
            }
        }

        return errors;
    }

    /// <summary>
    /// What the request asks for, for its <c>Idempotency-Key</c> (ADR 007): a retry must match it. It covers the
    /// amount, currency, reference, and the card's brand, last four, and expiry, but never the number or CVV, whose
    /// hash would be card data in itself (a card number has few enough candidates to brute-force).
    /// </summary>
    public byte[] Fingerprint()
    {
        ArgumentNullException.ThrowIfNull(Card);
        var number = Card.NormalizedNumber;
        // A JSON array, so no field's text can run into the next one's.
        var canonical = JsonSerializer.SerializeToUtf8Bytes(new object?[]
        {
            "POST /api/v1/payments",
            AmountMinor,
            Currency?.Trim().ToUpperInvariant(),
            MerchantReference?.Trim(),
            CardInput.BrandOf(number)?.ToString(),
            number[^4..],
            Card.ExpMonth,
            Card.ExpYear,
        });
        return SHA256.HashData(canonical);
    }
}

/// <summary>Who is creating the payment, from the validated token, and the correlation id of the request.</summary>
public sealed record PaymentCaller(MerchantId MerchantId, MerchantUserId UserId, string? CorrelationId);

/// <summary>
/// Creates a payment and authorizes it in the same request (docs/assumptions.md, assumption 7): the card is
/// tokenized at the edge, the payment is saved as <c>Pending</c> before the acquirer is called, and the answer
/// moves it to <c>Authorized</c> or <c>Failed</c>. If the acquirer does not answer, the payment stays
/// <c>Pending</c>, visibly, for reconciliation to finish; a crash between the two saves leaves the same.
/// </summary>
/// <remarks>
/// The acquirer is never called inside a database transaction, so a slow card network holds no locks; the two
/// saves are each one transaction, writing the payment and its audit rows together.
/// </remarks>
public sealed partial class CreatePayment(
    PaymentsDbContext db, ICardVault vault, IAcquirer acquirer, TimeProvider timeProvider, ILogger<CreatePayment> logger)
{
    /// <summary>Expects a request that passed <see cref="CreatePaymentRequest.Validate"/>.</summary>
    /// <param name="claim">
    /// The request's claimed <c>Idempotency-Key</c>, tracked by the same context: the payment is attached to it in
    /// the transaction that first saves the payment. If the key already names a payment (an abandoned claim taken
    /// over), that payment is finished instead of a second one being created.
    /// </param>
    public async Task<Result<PaymentDetails>> HandleAsync(
        CreatePaymentRequest request, PaymentCaller caller, IdempotencyClaim? claim, CancellationToken cancellationToken)
    {
        var idempotencyKey = claim?.Key;
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request.Card);

        if (idempotencyKey?.PaymentId is { } started)
        {
            return await ResumeAsync(started, caller, cancellationToken);
        }

        var card = vault.Tokenize(request.Card);
        if (!card.IsSuccess)
        {
            return card.Error;
        }

        var now = timeProvider.GetUtcNow();
        var amount = Money.Create(request.AmountMinor!.Value, request.Currency).Value;
        var payment = Payment.Create(
            PaymentId.New(now), caller.MerchantId, amount, card.Value, request.MerchantReference?.Trim(), ActorOf(caller), now);
        Activity.Current?.SetTag("payment.id", payment.Id.Value);

        db.Payments.Add(payment);
        idempotencyKey?.AttachPayment(payment.Id);
        var history = db.RecordChanges(payment, caller.CorrelationId).ToList();
        await db.SaveChangesAsync(cancellationToken);

        return await AuthorizeAsync(payment, history, caller, cancellationToken);
    }

    /// <summary>
    /// Finishes a payment an earlier request created and then lost: a <c>Pending</c> one is authorized again,
    /// which is safe because the payment id is the acquirer's idempotency key; any other is returned as it is.
    /// </summary>
    private async Task<Result<PaymentDetails>> ResumeAsync(PaymentId paymentId, PaymentCaller caller, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.SingleAsync(
            candidate => candidate.Id == paymentId && candidate.MerchantId == caller.MerchantId, cancellationToken);
        var history = await db.PaymentEvents.AsNoTracking()
            .Where(row => row.PaymentId == paymentId)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);
        LogResumed(logger, paymentId, payment.Status);
        return payment.Status == PaymentStatus.Pending
            ? await AuthorizeAsync(payment, history, caller, cancellationToken)
            : PaymentDetails.From(payment, history);
    }

    private async Task<Result<PaymentDetails>> AuthorizeAsync(
        Payment payment, List<PaymentEvent> history, PaymentCaller caller, CancellationToken cancellationToken)
    {
        var response = await acquirer.AuthorizeAsync(
            new AuthorizationRequest(payment.Id, payment.Amount, payment.Card.Token), cancellationToken);
        var actor = ActorOf(caller);
        var answeredAt = timeProvider.GetUtcNow();
        switch (response.Decision)
        {
            case AcquirerDecision.Approved:
                payment.Authorize(response.Reference!, actor, answeredAt);
                break;
            case AcquirerDecision.Declined:
                payment.Fail(response.DeclineCode!, actor, answeredAt);
                break;
            case AcquirerDecision.Unavailable:
                LogLeftPending(logger, payment.Id, caller.MerchantId);
                return PaymentDetails.From(payment, history);
        }

        history.AddRange(db.RecordChanges(payment, caller.CorrelationId));
        await db.SaveChangesAsync(cancellationToken);
        LogCreated(logger, payment.Id, caller.MerchantId, payment.Status);
        return PaymentDetails.From(payment, history);
    }

    private static Actor ActorOf(PaymentCaller caller) => Actor.MerchantUser(caller.UserId.Value);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} picked up again by a retry, as {Status}")]
    private static partial void LogResumed(ILogger logger, PaymentId paymentId, PaymentStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} for merchant {MerchantId} created: {Status}")]
    private static partial void LogCreated(ILogger logger, PaymentId paymentId, MerchantId merchantId, PaymentStatus status);

    // Not an error (the payment is safe and visible), but a person should know the card network is not answering.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Acquirer did not answer for payment {PaymentId} of merchant {MerchantId}; it stays pending for reconciliation")]
    private static partial void LogLeftPending(ILogger logger, PaymentId paymentId, MerchantId merchantId);
}
