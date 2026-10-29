using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
    }
}
