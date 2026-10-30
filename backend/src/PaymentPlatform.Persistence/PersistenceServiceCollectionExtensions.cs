using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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
                $"ConnectionStrings:{ConnectionStringName} is not set; docker-compose.yml shows the local value.");
        }

        services.AddSingleton<PaymentMetrics>();
        services.AddSingleton<PaymentTransitionInterceptor>();
        services.AddDbContextPool<PaymentsDbContext>((provider, options) =>
        {
            Configure(options, connectionString);
            options.AddInterceptors(provider.GetRequiredService<PaymentTransitionInterceptor>());
        });

        var probeOptions = new DbContextOptionsBuilder<PaymentsDbContext>();
        ConfigureProbe(probeOptions, connectionString);
        services.AddHealthChecks().AddCheck(
            HealthCheckName, new DatabaseHealthCheck(probeOptions.Options), timeout: DatabaseHealthCheck.Deadline);
        return services;
    }

    /// <summary>The provider settings, shared with the design-time factory so migrations match runtime.</summary>
    internal static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseSqlServer(connectionString, sql =>
            {
                UseMigrationsHistory(sql);
                sql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorNumbersToAdd: null);
                sql.CommandTimeout(30);
            })
            // EF Core logs every failed save at Error before the caller sees it, but several are the design working:
            // a retry meeting its own idempotency key, a row-version conflict a move or reconciliation re-reads.
            // A failure nothing handles still reaches Error once, from whoever catches it last: the API's exception
            // handler or the worker's job runner (ADR 005; ErrorLogTests).
            .ConfigureWarnings(warnings => warnings.Log((CoreEventId.SaveChangesFailed, LogLevel.Debug)));

    /// <summary>
    /// The readiness probe's settings: one attempt, no transient-fault retries, and connect and command
    /// timeouts inside <see cref="DatabaseHealthCheck.Deadline"/>.
    /// </summary>
    internal static void ConfigureProbe(DbContextOptionsBuilder options, string connectionString)
    {
        var probe = new SqlConnectionStringBuilder(connectionString) { ConnectTimeout = 2, ConnectRetryCount = 0 };
        options.UseSqlServer(probe.ConnectionString, sql =>
        {
            UseMigrationsHistory(sql);
            sql.CommandTimeout(2);
        });
    }

    private static void UseMigrationsHistory(SqlServerDbContextOptionsBuilder sql) =>
        sql.MigrationsHistoryTable("__EFMigrationsHistory", PaymentsDbContext.Schema);
}
