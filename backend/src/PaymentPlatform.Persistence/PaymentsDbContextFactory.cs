using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaymentPlatform.Persistence;

/// <summary>
/// Lets <c>dotnet ef</c> build the context without starting a host. Adding a migration never connects;
/// commands that do (<c>database update</c>) read <c>ConnectionStrings__Payments</c> from the environment.
/// </summary>
internal sealed class PaymentsDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    // Enough for `migrations add`, which never connects; it deliberately holds no credentials, so commands
    // that do connect fail until the environment variable is set.
    private const string OfflineConnectionString = "Server=localhost,14333;Database=payments;TrustServerCertificate=True";

    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable($"ConnectionStrings__{PersistenceServiceCollectionExtensions.ConnectionStringName}");
        var options = new DbContextOptionsBuilder<PaymentsDbContext>();
        PersistenceServiceCollectionExtensions.Configure(options, string.IsNullOrWhiteSpace(connectionString) ? OfflineConnectionString : connectionString);
        return new PaymentsDbContext(options.Options);
    }
}
