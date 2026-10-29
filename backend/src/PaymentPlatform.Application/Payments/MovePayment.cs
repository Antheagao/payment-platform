using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Payments;

/// <summary>The errors capture and refund return, beyond the lifecycle's own <c>payment_not_capturable</c> and the like.</summary>
public static class PaymentErrors
{
    public static readonly DomainError NotFound = new("not_found", "no payment with this id");

    public static readonly DomainError AcquirerUnavailable = new(
        "acquirer_unavailable", "the card network did not answer; the payment is unchanged, and the same request may be retried");

    public static DomainError Declined(string operation, string declineCode) =>
        new($"{operation}_declined", $"the card network declined the {operation} ({declineCode}); the payment is unchanged");
}

/// <summary>
/// Capture and refund: one move a merchant asks for on an existing payment, checked against the lifecycle
/// before the card network is called, sent to it outside any transaction, and saved with its audit row only
/// if the network approves. A decline or no answer leaves the payment exactly as it was.
/// </summary>
/// <remarks>
/// Two writers can change a payment between this move's read and its save; the row version catches it, and the
/// move is tried again against the payment as it now is. If the move is still legal it is saved: the acquirer
/// approved it, so it must be recorded (a refund racing settlement refunds a settled payment). If not, it is the
/// lifecycle's conflict, as if it had arrived a moment later; two captures or two refunds end that way, and since
/// the acquirer call is idempotent by payment id, the loser's call moved no money twice.
/// </remarks>
public abstract partial class MovePayment(PaymentsDbContext db, IAcquirer acquirer, TimeProvider timeProvider, ILogger logger)
{
    /// <summary>The operation's name in codes and logs: <c>capture</c>, <c>refund</c>.</summary>
    protected abstract string Operation { get; }

    // A payment changes a handful of times in its life, so a few rereads always reach a stable state.
    private const int MaxSaveAttempts = 3;

    protected abstract PaymentStatus Target { get; }

    public async Task<Result<PaymentDetails>> HandleAsync(PaymentId id, PaymentCaller caller, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(caller);
        Activity.Current?.SetTag("payment.id", id.Value);
        var payment = await db.Payments.SingleOrDefaultAsync(
            candidate => candidate.Id == id && candidate.MerchantId == caller.MerchantId, cancellationToken);
        if (payment is null)
        {
            return PaymentErrors.NotFound;
        }

        if (!PaymentLifecycle.CanTransition(payment.Status, Target))
        {
            return PaymentLifecycle.InvalidTransition(payment.Status, Target);
        }

        var response = await CallAcquirerAsync(
            new FollowUpRequest(payment.Id, payment.Amount, payment.AuthorizationReference!), cancellationToken);
        switch (response.Decision)
        {
            case AcquirerDecision.Declined:
                LogDeclined(logger, Operation, payment.Id, response.DeclineCode!);
                return PaymentErrors.Declined(Operation, response.DeclineCode!);
            case AcquirerDecision.Unavailable:
                LogUnavailable(logger, Operation, payment.Id);
                return PaymentErrors.AcquirerUnavailable;
        }

        for (var attempt = 1; ; attempt++)
        {
            var moved = Apply(payment, Actor.MerchantUser(caller.UserId.Value), timeProvider.GetUtcNow());
            if (!moved.IsSuccess)
            {
                return moved.Error;
            }

            db.RecordChanges(payment, caller.CorrelationId);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                break;
            }
            catch (DbUpdateConcurrencyException) when (attempt < MaxSaveAttempts)
            {
                // Another writer changed the payment between this move's read and its save: read it again.
                db.ChangeTracker.Clear();
                payment = await db.Payments.SingleAsync(candidate => candidate.Id == id, cancellationToken);
                if (!PaymentLifecycle.CanTransition(payment.Status, Target))
                {
                    return PaymentLifecycle.InvalidTransition(payment.Status, Target);
                }

                LogRetried(logger, Operation, payment.Id, payment.Status);
            }
        }

        LogMoved(logger, Operation, payment.Id, caller.MerchantId);
        var history = await db.PaymentEvents.AsNoTracking()
            .Where(row => row.PaymentId == id).OrderBy(row => row.Id).ToListAsync(cancellationToken);
        return PaymentDetails.From(payment, history);
    }

    protected abstract Task<AcquirerResponse> CallAcquirerAsync(FollowUpRequest request, CancellationToken cancellationToken);

    protected abstract Result Apply(Payment payment, Actor actor, DateTimeOffset now);

    protected IAcquirer Acquirer => acquirer;

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} of merchant {MerchantId}: {Operation} done")]
    private static partial void LogMoved(ILogger logger, string operation, PaymentId paymentId, MerchantId merchantId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} changed to {Status} during the {Operation}; the {Operation} is saved against its new state")]
    private static partial void LogRetried(ILogger logger, string operation, PaymentId paymentId, PaymentStatus status);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId}: the acquirer declined the {Operation} ({DeclineCode})")]
    private static partial void LogDeclined(ILogger logger, string operation, PaymentId paymentId, string declineCode);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Payment {PaymentId}: the acquirer did not answer the {Operation}; nothing changed")]
    private static partial void LogUnavailable(ILogger logger, string operation, PaymentId paymentId);
}

/// <summary>Claims an authorized payment's funds (assumption 8: the full authorized amount).</summary>
public sealed class CapturePayment(PaymentsDbContext db, IAcquirer acquirer, TimeProvider timeProvider, ILogger<CapturePayment> logger)
    : MovePayment(db, acquirer, timeProvider, logger)
{
    protected override string Operation => "capture";

    protected override PaymentStatus Target => PaymentStatus.Captured;

    protected override Task<AcquirerResponse> CallAcquirerAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
        Acquirer.CaptureAsync(request, cancellationToken);

    protected override Result Apply(Payment payment, Actor actor, DateTimeOffset now) => payment.Capture(actor, now);
}

/// <summary>Returns a captured or settled payment's funds in full (assumption 9).</summary>
public sealed class RefundPayment(PaymentsDbContext db, IAcquirer acquirer, TimeProvider timeProvider, ILogger<RefundPayment> logger)
    : MovePayment(db, acquirer, timeProvider, logger)
{
    protected override string Operation => "refund";

    protected override PaymentStatus Target => PaymentStatus.Refunded;

    protected override Task<AcquirerResponse> CallAcquirerAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
        Acquirer.RefundAsync(request, cancellationToken);

    protected override Result Apply(Payment payment, Actor actor, DateTimeOffset now) => payment.Refund(actor, now);
}
