using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;

namespace PaymentPlatform.Domain.Payments;

/// <summary>
/// A payment and its lifecycle. Status changes only through the methods below, each of which checks
/// <see cref="PaymentLifecycle"/> and records a <see cref="PaymentStatusChanged"/>; there is no status setter.
/// </summary>
public sealed class Payment
{
    private readonly List<PaymentStatusChanged> _pendingEvents = [];

    private Payment(PaymentId id, MerchantId merchantId, Money amount, DateTimeOffset createdAt)
    {
        Id = id;
        MerchantId = merchantId;
        Amount = amount;
        Status = PaymentStatus.Pending;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public PaymentId Id { get; }

    public MerchantId MerchantId { get; }

    public Money Amount { get; }

    public PaymentStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Changes not yet saved; persistence drains them with <see cref="DequeueEvents"/>.</summary>
    public IReadOnlyList<PaymentStatusChanged> PendingEvents => _pendingEvents;

    public static Payment Create(PaymentId id, MerchantId merchantId, Money amount, Actor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        var payment = new Payment(id, merchantId, amount, now);
        payment._pendingEvents.Add(new PaymentStatusChanged(id, merchantId, null, PaymentStatus.Pending, actor, now, null));
        return payment;
    }

    public Result Authorize(Actor actor, DateTimeOffset now) => MoveTo(PaymentStatus.Authorized, actor, now, null);

    /// <param name="reason">The acquirer's decline code, kept on the audit row.</param>
    public Result Fail(string reason, Actor actor, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        return MoveTo(PaymentStatus.Failed, actor, now, reason);
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
