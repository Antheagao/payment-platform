using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PaymentPlatform.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>The connection string name: <c>ConnectionStrings:Payments</c>, or <c>ConnectionStrings__Payments</c> in the environment.</summary>
    public const string ConnectionStringName = "Payments";

    /// <summary>The health check name on <c>/health/ready</c>.</summary>
    public const string HealthCheckName = "database";

    /// <summary>
    /// Registers <see cref="PaymentsDbContext"/> on SQL Server and its readiness check; a missing connection
    /// string fails startup rather than the first request.
    /// </summary>
    /// <remarks>
    /// Transient-fault retries are on, as Azure SQL needs; a use case that opens its own transaction must
    /// therefore run it inside <c>Database.CreateExecutionStrategy().ExecuteAsync(...)</c>, or EF Core refuses.
    /// </remarks>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"ConnectionStrings:{ConnectionStringName} is not set; see .env.example for the local value.");
        }

        services.AddDbContextPool<PaymentsDbContext>(options => Configure(options, connectionString));
        // A probe must answer quickly even when the server is gone, so the check gets its own short timeout.
        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>(HealthCheckName, timeout: TimeSpan.FromSeconds(5));
        return services;
    }

    /// <summary>The provider settings, shared with the design-time factory so migrations match runtime.</summary>
    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseSqlServer(connectionString, sql =>
        {
            sql.MigrationsHistoryTable("__EFMigrationsHistory", PaymentsDbContext.Schema);
            sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
            sql.CommandTimeout(30);
        });
}
