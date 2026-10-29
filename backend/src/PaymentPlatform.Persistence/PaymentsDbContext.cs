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

    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<MerchantUser> MerchantUsers => Set<MerchantUser>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    /// <summary>
    /// Turns the payment's unsaved status changes into audit rows, stamped with the correlation id of the request
    /// or job making them, so the next save writes the change and its trail in one transaction.
    /// </summary>
    public IReadOnlyList<PaymentEvent> RecordChanges(Payment payment, string? correlationId)
    {
        ArgumentNullException.ThrowIfNull(payment);
        var rows = payment.DequeueEvents().Select(change => PaymentEvent.From(change, correlationId)).ToList();
        PaymentEvents.AddRange(rows);
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
