using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace PaymentPlatform.Persistence;

/// <summary>
/// Applies pending migrations at startup, for local runs only (<c>Database:MigrateOnStartup</c>).
/// </summary>
/// <remarks>
/// Only the API ever calls this, so two processes never race to migrate. In a deployment, migrations run as a
/// pipeline step before the new build takes traffic, and the readiness check keeps an instance out of rotation
/// until its schema is current.
/// </remarks>
public static partial class DatabaseMigrator
{
    public const string SettingName = "Database:MigrateOnStartup";

    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseMigrator));

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        await db.Database.MigrateAsync(cancellationToken);
        LogMigrated(logger, pending.Count);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Database migrated at startup: {AppliedCount} migrations applied")]
    private static partial void LogMigrated(ILogger logger, int appliedCount);
}
