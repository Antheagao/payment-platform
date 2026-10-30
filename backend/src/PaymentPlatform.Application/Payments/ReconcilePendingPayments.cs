using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Payments;

/// <summary>The <c>Reconciliation</c> configuration section.</summary>
public sealed class ReconciliationOptions
{
    public const string Section = "Reconciliation";

    /// <summary>
    /// How long a payment is left to the request that created it before reconciliation asks: well past the API's
    /// 10 s request timeout, so the two never race over a payment still being authorized.
    /// </summary>
    [Range(typeof(TimeSpan), "00:00:30", "01:00:00")]
    public TimeSpan GracePeriod { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>How long to wait before asking again about a payment the acquirer did not answer for.</summary>
    [Range(typeof(TimeSpan), "00:00:10", "06:00:00")]
    public TimeSpan RetryInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long a payment may stay unanswered before it fails as <c>acquirer_unreachable</c>: about as long as the
    /// card networks keep an authorization, so a hold placed without our knowing has lapsed by then too.
    /// </summary>
    [Range(typeof(TimeSpan), "00:10:00", "7.00:00:00")]
    public TimeSpan GiveUpAfter { get; set; } = TimeSpan.FromDays(1);

    [Range(1, 1_000)]
    public int BatchSize { get; set; } = 50;
}

/// <summary>What one batch did with the payments it found.</summary>
public sealed record ReconciliationRun(int Authorized, int Failed, int StillPending, int TakenElsewhere)
{
    /// <summary>How many payments the batch looked at, the number that decides whether another batch follows.</summary>
    public int Count => Authorized + Failed + StillPending + TakenElsewhere;
}

/// <summary>
/// Finishes payments left <c>Pending</c> (docs/assumptions.md, assumption 7): the acquirer did not answer during
/// create, or the process stopped between its answer and the save. Each one past the grace period is authorized
/// again, which is safe because the payment id is the acquirer's idempotency key, and moves to <c>Authorized</c> or
/// <c>Failed</c> with its audit row and event, as the <c>system</c> actor <c>reconciliation</c>.
/// </summary>
/// <remarks>
/// The acquirer is called outside any transaction, as everywhere. Two workers may pick the same payment and both
/// ask; the acquirer answers both alike, and the row version lets only one save, so the change is recorded once.
/// The same holds against a create request resuming the payment after a crash: if reconciliation saves first, that
/// request's save fails, its idempotency key is released, and its retry answers with the reconciled payment.
/// A payment the acquirer still does not answer for is asked about again after the retry interval, so the stuck
/// ones never crowd out newer ones, and fails once it has waited longer than the networks would hold its funds.
/// </remarks>
public sealed partial class ReconcilePendingPayments(
    PaymentsDbContext db,
    IAcquirer acquirer,
    IOptions<ReconciliationOptions> options,
    TimeProvider timeProvider,
    ILogger<ReconcilePendingPayments> logger)
{
    public const string ActorName = "reconciliation";

    public const string UnreachableCode = "acquirer_unreachable";

    private static readonly Actor Reconciler = Actor.System(ActorName);

    /// <summary>
    /// How much earlier than one interval on a payment counts as due: the job's runs start a few milliseconds apart
    /// from one minute to the next, and without this the next run could miss a payment by that much and leave it
    /// for the one after (seen on the composed stack).
    /// </summary>
    private static readonly TimeSpan ScheduleTolerance = TimeSpan.FromSeconds(5);

    public async Task<ReconciliationRun> HandleAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var now = timeProvider.GetUtcNow();
        var ids = await Due(db.Payments.AsNoTracking(), now, settings.GracePeriod)
            .OrderBy(payment => payment.CreatedAt)
            .Select(payment => payment.Id)
            .Take(settings.BatchSize)
            .ToListAsync(cancellationToken);

        var correlationId = $"reconciliation-{Guid.CreateVersion7(now):N}";
        int authorized = 0, failed = 0, stillPending = 0, takenElsewhere = 0;
        foreach (var id in ids)
        {
            switch (await ReconcileAsync(id, now, correlationId, settings, cancellationToken))
            {
                case PaymentStatus.Authorized:
                    authorized++;
                    break;
                case PaymentStatus.Failed:
                    failed++;
                    break;
                case PaymentStatus.Pending:
                    stillPending++;
                    break;
                default:
                    takenElsewhere++;
                    break;
            }
        }

        return new ReconciliationRun(authorized, failed, stillPending, takenElsewhere);
    }

    /// <summary>
    /// The payments due for reconciliation at <paramref name="now"/>: <c>Pending</c> past the grace period, and not
    /// waiting out a retry interval. The queue gauge counts the same rows.
    /// </summary>
    public static IQueryable<Payment> Due(IQueryable<Payment> payments, DateTimeOffset now, TimeSpan gracePeriod)
    {
        ArgumentNullException.ThrowIfNull(payments);
        var createdBefore = now - gracePeriod;
        return payments.Where(payment =>
            payment.Status == PaymentStatus.Pending
            && payment.CreatedAt < createdBefore
            && (EF.Property<DateTimeOffset?>(payment, PaymentsDbContext.PaymentReconcileAfter) == null
                || EF.Property<DateTimeOffset?>(payment, PaymentsDbContext.PaymentReconcileAfter) <= now));
    }

    /// <summary>Returns the payment's status afterwards, or null when another writer changed it first.</summary>
    /// <param name="runStartedAt">
    /// When this batch looked for due payments: the next attempt counts from it, not from the acquirer's answer, so
    /// the run one interval later finds the payment due rather than missing it by the length of this one.
    /// </param>
    private async Task<PaymentStatus?> ReconcileAsync(
        PaymentId id, DateTimeOffset runStartedAt, string correlationId, ReconciliationOptions settings, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var payment = await db.Payments.SingleAsync(candidate => candidate.Id == id, cancellationToken);
        if (payment.Status != PaymentStatus.Pending)
        {
            return null;
        }

        var response = await acquirer.AuthorizeAsync(
            new AuthorizationRequest(payment.Id, payment.Amount, payment.Card.Token), cancellationToken);
        var answeredAt = timeProvider.GetUtcNow();
        var age = answeredAt - payment.CreatedAt;
        var nextAttemptAt = NextAttemptAt(runStartedAt, settings);
        switch (response.Decision)
        {
            case AcquirerDecision.Approved:
                payment.Authorize(response.Reference!, Reconciler, answeredAt);
                break;
            case AcquirerDecision.Declined:
                payment.Fail(response.DeclineCode!, Reconciler, answeredAt);
                break;
            case AcquirerDecision.Unavailable when age >= settings.GiveUpAfter:
                payment.Fail(UnreachableCode, Reconciler, answeredAt);
                break;
            case AcquirerDecision.Unavailable:
                db.Entry(payment).Property(PaymentsDbContext.PaymentReconcileAfter).CurrentValue = nextAttemptAt;
                break;
        }

        db.RecordChanges(payment, correlationId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another worker, or a retried create request, saved the payment after it was read here; theirs stands.
            db.ChangeTracker.Clear();
            LogTakenElsewhere(logger, payment.Id);
            return null;
        }

        if (payment.Status == PaymentStatus.Pending)
        {
            LogStillPending(logger, payment.Id, age, nextAttemptAt);
        }
        else if (payment.FailureReason == UnreachableCode)
        {
            LogGaveUp(logger, payment.Id, age);
        }
        else
        {
            LogReconciled(logger, payment.Id, payment.Status, correlationId);
        }

        return payment.Status;
    }

    private static DateTimeOffset NextAttemptAt(DateTimeOffset runStartedAt, ReconciliationOptions settings) =>
        runStartedAt + settings.RetryInterval - ScheduleTolerance;

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} reconciled: {Status} (run {CorrelationId})")]
    private static partial void LogReconciled(ILogger logger, PaymentId paymentId, PaymentStatus status, string correlationId);

    // Information, not Warning: create warned when the acquirer first did not answer, and a retry every few minutes
    // for a day would bury that warning; the queue gauge and the give-up error are the signals to act on.
    [LoggerMessage(Level = LogLevel.Information, Message = "Acquirer still did not answer for payment {PaymentId}, pending for {Age}; asking again after {NextAttemptAt}")]
    private static partial void LogStillPending(ILogger logger, PaymentId paymentId, TimeSpan age, DateTimeOffset nextAttemptAt);

    // The acquirer may have authorized it without our hearing, so a person checks with them; hence Error (ADR 005).
    [LoggerMessage(Level = LogLevel.Error, Message = "Payment {PaymentId} failed as acquirer_unreachable after {Age} unanswered; check it with the acquirer")]
    private static partial void LogGaveUp(ILogger logger, PaymentId paymentId, TimeSpan age);

    [LoggerMessage(Level = LogLevel.Information, Message = "Payment {PaymentId} was changed by another writer during reconciliation; its change stands")]
    private static partial void LogTakenElsewhere(ILogger logger, PaymentId paymentId);
}
