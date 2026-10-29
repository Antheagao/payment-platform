using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Settlement;

/// <summary>The <c>Settlement</c> configuration section.</summary>
public sealed class SettlementOptions
{
    public const string Section = "Settlement";

    /// <summary>How long a captured payment waits before it settles: a day in production, a minute in compose.</summary>
    [Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]
    public TimeSpan Delay { get; set; } = TimeSpan.FromDays(1);

    [Range(1, 5_000)]
    public int BatchSize { get; set; } = 500;
}

/// <summary>One batch's count and amount in one currency, as the procedure reports them.</summary>
public sealed class SettlementTotal
{
    public required string Currency { get; init; }

    public required int Payments { get; init; }

    public required long AmountMinor { get; init; }
}

/// <summary>
/// Settles captured payments through <c>payments.SettleCapturedPayments</c> (ADR 003): set-based work over many rows,
/// which is what T-SQL is for, where the rest of the platform goes through EF Core. Each batch's events carry the
/// batch's correlation id and this run's trace, so their webhooks join the settlement's trace.
/// </summary>
public sealed partial class SettlePayments(
    PaymentsDbContext db, PaymentMetrics metrics, IOptions<SettlementOptions> options, TimeProvider timeProvider, ILogger<SettlePayments> logger)
{
    /// <summary>Settles one batch and returns its totals per currency; an empty list when nothing was due.</summary>
    public async Task<IReadOnlyList<SettlementTotal>> HandleAsync(CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var capturedBefore = now - options.Value.Delay;
        var batchSize = options.Value.BatchSize;
        var correlationId = $"settlement-{Guid.CreateVersion7(now):N}";
        var traceParent = Activity.Current?.Id;
        var totals = await db.Database.SqlQuery<SettlementTotal>($"""
            EXEC payments.SettleCapturedPayments
                @Now = {now}, @CapturedBefore = {capturedBefore}, @BatchSize = {batchSize},
                @CorrelationId = {correlationId}, @TraceParent = {traceParent}
            """).ToListAsync(cancellationToken);
        foreach (var total in totals)
        {
            LogSettled(logger, total.Payments, total.Currency, total.AmountMinor, correlationId);
        }

        // The procedure writes past EF Core, so its transitions are counted here rather than by the save interceptor.
        metrics.RecordTransitions(Domain.Payments.PaymentStatus.Captured, Domain.Payments.PaymentStatus.Settled, CountOf(totals));

        return totals;
    }

    /// <summary>How many payments the batch held, the number that decides whether another batch follows.</summary>
    public static int CountOf(IReadOnlyList<SettlementTotal> totals) => totals?.Sum(total => total.Payments) ?? 0;

    [LoggerMessage(Level = LogLevel.Information, Message = "Settled {PaymentCount} {Currency} payments for {AmountMinor} minor units (batch {CorrelationId})")]
    private static partial void LogSettled(ILogger logger, int paymentCount, string currency, long amountMinor, string correlationId);
}
