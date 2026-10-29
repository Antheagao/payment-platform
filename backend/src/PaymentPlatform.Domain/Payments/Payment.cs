using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;

namespace PaymentPlatform.Domain.Payments;

/// <summary>
/// A payment and its lifecycle. Status changes only through the methods below, each of which checks
/// <see cref="PaymentLifecycle"/> and records a <see cref="PaymentStatusChanged"/>; there is no status setter.
/// </summary>
public sealed class Payment
{
    /// <summary>The longest merchant reference (an order number, an invoice id) the API accepts and the column holds.</summary>
    public const int MerchantReferenceMaxLength = 100;

    private readonly List<PaymentStatusChanged> _pendingEvents = [];

    private Payment(
        PaymentId id, MerchantId merchantId, Money amount, PaymentCard card, string? merchantReference, DateTimeOffset createdAt)
    {
        Id = id;
        MerchantId = merchantId;
        Amount = amount;
        Card = card;
        MerchantReference = merchantReference;
        Status = PaymentStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    // For EF Core, which sets every property from its column.
    private Payment()
    {
        Card = null!;
    }

    public PaymentId Id { get; }

    public MerchantId MerchantId { get; }

    public Money Amount { get; }

    public PaymentCard Card { get; }

    /// <summary>The merchant's own id for what was paid for, if they gave one; searchable, never unique.</summary>
    public string? MerchantReference { get; }

    public PaymentStatus Status { get; private set; }

    /// <summary>The acquirer's reference for the authorization, which capture and refund quote back to it.</summary>
    public string? AuthorizationReference { get; private set; }

    /// <summary>Why the acquirer declined, such as <c>insufficient_funds</c>; set only on a failed payment.</summary>
    public string? FailureReason { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Changes not yet saved; persistence drains them with <see cref="DequeueEvents"/>.</summary>
    public IReadOnlyList<PaymentStatusChanged> PendingEvents => _pendingEvents;

    /// <exception cref="ArgumentException">The merchant reference is blank or longer than <see cref="MerchantReferenceMaxLength"/>.</exception>
    public static Payment Create(
        PaymentId id, MerchantId merchantId, Money amount, PaymentCard card, string? merchantReference, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(actor);
        if (merchantReference is not null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(merchantReference);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(merchantReference.Length, MerchantReferenceMaxLength, nameof(merchantReference));
        }

        var payment = new Payment(id, merchantId, amount, card, merchantReference, now);
        payment._pendingEvents.Add(new PaymentStatusChanged(id, merchantId, null, PaymentStatus.Pending, actor, now, null));
        return payment;
    }

    /// <param name="authorizationReference">The acquirer's reference for the approval.</param>
    public Result Authorize(string authorizationReference, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationReference);
        var moved = MoveTo(PaymentStatus.Authorized, actor, now, null);
        if (moved.IsSuccess)
        {
            AuthorizationReference = authorizationReference;
        }

        return moved;
    }

    /// <param name="reason">The acquirer's decline code, kept on the payment and its audit row.</param>
    public Result Fail(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var moved = MoveTo(PaymentStatus.Failed, actor, now, reason);
        if (moved.IsSuccess)
        {
            FailureReason = reason;
        }

        return moved;
    }

    public Result Capture(Actor actor, DateTimeOffset now) => MoveTo(PaymentStatus.Captured, actor, now, null);

    public Result Settle(Actor actor, DateTimeOffset now) => MoveTo(PaymentStatus.Settled, actor, now, null);

    public Result Refund(Actor actor, DateTimeOffset now) => MoveTo(PaymentStatus.Refunded, actor, now, null);

    /// <summary>Returns the unsaved changes and forgets them; called once, by persistence, while saving.</summary>
    public IReadOnlyList<PaymentStatusChanged> DequeueEvents()
    {
        var events = _pendingEvents.ToArray();
        _pendingEvents.Clear();
        return events;
    }

    private Result MoveTo(PaymentStatus to, Actor actor, DateTimeOffset now, string? reason)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!PaymentLifecycle.CanTransition(Status, to))
        {
            return PaymentLifecycle.InvalidTransition(Status, to);
        }

        _pendingEvents.Add(new PaymentStatusChanged(Id, MerchantId, Status, to, actor, now, reason));
        Status = to;
        UpdatedAt = now;
        return Result.Success();
    }
}
