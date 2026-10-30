using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence;

/// <summary>
/// The platform's one database. Use cases work with it directly; there is no repository layer (ADR 002).
/// </summary>
/// <remarks>
/// Every table lives in the <c>payments</c> schema, including EF Core's migration history, so the database
/// can share a server with other applications without colliding in <c>dbo</c>.
/// </remarks>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public const string Schema = "payments";

    /// <summary>
    /// The payments' shadow identity column: it orders payments created at the same instant, as the list's keyset
    /// and its clustered index do. Read it in a query with <c>EF.Property&lt;long&gt;(payment, PaymentSequence)</c>.
    /// </summary>
    public const string PaymentSequence = "Sequence";

    /// <summary>
    /// A <c>Pending</c> payment's shadow column: when reconciliation may ask the acquirer about it again, after an
    /// attempt the acquirer did not answer; null until the first. Bookkeeping, not part of the payment, so it is no
    /// property of the domain type.
    /// </summary>
    public const string PaymentReconcileAfter = "ReconcileAfter";

    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<MerchantUser> MerchantUsers => Set<MerchantUser>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<WebhookEndpoint> WebhookEndpoints => Set<WebhookEndpoint>();

    public DbSet<WebhookDelivery> WebhookDeliveries => Set<WebhookDelivery>();

    /// <summary>
    /// Turns the payment's unsaved status changes into audit rows and, for each change after creation, an outbox
    /// message (ADR 003), all stamped with the correlation id of the request or job making them, so the next save
    /// writes the change, its trail, and its event in one transaction.
    /// </summary>
    /// <param name="publish">
    /// False only for history that is not news, such as the demo seed's sample payments: their audit rows are
    /// written, and no webhook goes out for events that never happened.
    /// </param>
    public IReadOnlyList<PaymentEvent> RecordChanges(Payment payment, string? correlationId, bool publish = true)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var changes = payment.DequeueEvents();
        var rows = changes.Select(change =>
        {
            var row = PaymentEvent.From(change, correlationId);
            row.IsNews = publish;
            return row;
        }).ToList();
        PaymentEvents.AddRange(rows);
        if (publish)
        {
            OutboxMessages.AddRange(changes
                .Select(change => OutboxMessage.For(payment, change, correlationId, change.OccurredAt))
                .OfType<OutboxMessage>());
        }

        return rows;
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        RefuseUnrecordedChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        RefuseUnrecordedChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }

    // Every status change is audited (R24): saving a payment whose changes were never recorded is a bug in the
    // use case, so it fails loudly instead of writing a status with no trail.
    private void RefuseUnrecordedChanges()
    {
        var unrecorded = ChangeTracker.Entries<Payment>().FirstOrDefault(entry => entry.Entity.PendingEvents.Count > 0);
        if (unrecorded is not null)
        {
            throw new InvalidOperationException(
                $"payment {unrecorded.Entity.Id} has status changes with no audit rows; call {nameof(RecordChanges)} before saving");
        }
    }
}
