using System.Diagnostics;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Cards;
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
    public async Task<Result<PaymentDetails>> HandleAsync(
        CreatePaymentRequest request, PaymentCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(caller);
        ArgumentNullException.ThrowIfNull(request.Card);

        var card = vault.Tokenize(request.Card);
        if (!card.IsSuccess)
        {
            return card.Error;
        }

        var now = timeProvider.GetUtcNow();
        var actor = Actor.MerchantUser(caller.UserId.Value);
        var amount = Money.Create(request.AmountMinor!.Value, request.Currency).Value;
        var payment = Payment.Create(
            PaymentId.New(now), caller.MerchantId, amount, card.Value, request.MerchantReference?.Trim(), actor, now);
        Activity.Current?.SetTag("payment.id", payment.Id.Value);

        db.Payments.Add(payment);
        var history = db.RecordChanges(payment, caller.CorrelationId).ToList();
        await db.SaveChangesAsync(cancellationToken);

        var response = await acquirer.AuthorizeAsync(new AuthorizationRequest(payment.Id, amount, card.Value.Token), cancellationToken);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} for merchant {MerchantId} created: {Status}")]
    private static partial void LogCreated(ILogger logger, PaymentId paymentId, MerchantId merchantId, PaymentStatus status);

    // Not an error (the payment is safe and visible), but a person should know the card network is not answering.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Acquirer did not answer for payment {PaymentId} of merchant {MerchantId}; it stays pending for reconciliation")]
    private static partial void LogLeftPending(ILogger logger, PaymentId paymentId, MerchantId merchantId);
}
