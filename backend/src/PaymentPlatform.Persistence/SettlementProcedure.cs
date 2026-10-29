namespace PaymentPlatform.Persistence;

/// <summary>
/// <c>payments.SettleCapturedPayments</c>, created by the <c>Settlement</c> migration (ADR 003). In one transaction it
/// moves a batch of payments captured before <c>@CapturedBefore</c> to <c>Settled</c>, writes each one's audit row
/// (actor <c>System</c>, <c>settlement</c>) and its <c>payment.settled</c> outbox event, and returns the batch's count
/// and amount per currency, for reconciliation.
/// </summary>
/// <remarks>
/// The domain's rule, that only a captured payment settles, is the <c>WHERE</c>; the event's payload is built in the
/// shape <see cref="OutboxMessage"/> writes from C# (lowercase ids, nulls included), and a test holds the two to each
/// other. <c>UPDLOCK, READPAST</c> lets several workers run it at once, and the row version the update changes makes a
/// capture or refund that read the payment before it retry against its new state.
/// </remarks>
public static class SettlementProcedure
{
    public const string Name = "payments.SettleCapturedPayments";

    /// <summary>
    /// The procedure as the <c>Settlement</c> migration creates it. Never edited: a migration must replay the same SQL
    /// forever, so a change is a new constant and a new migration that runs it.
    /// </summary>
    public const string Version1 = """
        CREATE OR ALTER PROCEDURE payments.SettleCapturedPayments
            @Now datetimeoffset(7),
            @CapturedBefore datetimeoffset(7),
            @BatchSize int,
            @CorrelationId varchar(64),
            @TraceParent varchar(55)
        AS
        BEGIN
            SET NOCOUNT ON;
            SET XACT_ABORT ON;

            DECLARE @Settled TABLE (
                Id uniqueidentifier PRIMARY KEY,
                MerchantId uniqueidentifier NOT NULL,
                AmountMinor bigint NOT NULL,
                Currency char(3) NOT NULL,
                MerchantReference nvarchar(100) NULL,
                FailureReason varchar(64) NULL,
                EventId uniqueidentifier NOT NULL DEFAULT NEWID());

            BEGIN TRANSACTION;

            WITH due AS (
                SELECT TOP (@BatchSize) Id, MerchantId, Status, UpdatedAt, AmountMinor, Currency, MerchantReference, FailureReason
                FROM payments.Payments WITH (UPDLOCK, READPAST, ROWLOCK)
                WHERE Status = 'Captured' AND UpdatedAt < @CapturedBefore
                ORDER BY UpdatedAt)
            UPDATE due SET Status = 'Settled', UpdatedAt = @Now
            OUTPUT inserted.Id, inserted.MerchantId, inserted.AmountMinor, inserted.Currency, inserted.MerchantReference, inserted.FailureReason
            INTO @Settled (Id, MerchantId, AmountMinor, Currency, MerchantReference, FailureReason);

            INSERT payments.PaymentEvents (PaymentId, MerchantId, FromStatus, ToStatus, ActorKind, ActorId, OccurredAt, Reason, CorrelationId)
            SELECT Id, MerchantId, 'Captured', 'Settled', 'System', N'settlement', @Now, NULL, @CorrelationId
            FROM @Settled;

            INSERT payments.OutboxMessages (EventId, MerchantId, PaymentId, EventType, Payload, CorrelationId, TraceParent, OccurredAt, DispatchedAt)
            SELECT s.EventId, s.MerchantId, s.Id, 'payment.settled',
                (SELECT
                    LOWER(CONVERT(char(36), s.EventId)) AS id,
                    'payment.settled' AS [type],
                    @Now AS created_at,
                    LOWER(CONVERT(char(36), s.Id)) AS [data.payment_id],
                    'settled' AS [data.status],
                    'captured' AS [data.previous_status],
                    s.AmountMinor AS [data.amount_minor],
                    s.Currency AS [data.currency],
                    s.MerchantReference AS [data.merchant_reference],
                    s.FailureReason AS [data.failure_reason]
                 FOR JSON PATH, WITHOUT_ARRAY_WRAPPER, INCLUDE_NULL_VALUES),
                @CorrelationId, @TraceParent, @Now, NULL
            FROM @Settled AS s;

            COMMIT TRANSACTION;

            SELECT Currency, COUNT(*) AS Payments, SUM(AmountMinor) AS AmountMinor
            FROM @Settled
            GROUP BY Currency;
        END
        """;

    public const string DropSql = "DROP PROCEDURE IF EXISTS payments.SettleCapturedPayments;";

    /// <summary>
    /// <paramref name="definition"/> run through <c>EXEC</c>: an idempotent migration script wraps each step in
    /// <c>IF NOT EXISTS (...) BEGIN ... END</c>, where a bare <c>CREATE PROCEDURE</c> is a syntax error, because it must
    /// be the only statement in its batch.
    /// </summary>
    public static string AsExec(string definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return $"EXEC(N'{definition.Replace("'", "''", StringComparison.Ordinal)}');";
    }
}
