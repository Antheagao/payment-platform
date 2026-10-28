using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace PaymentPlatform.Persistence;

/// <summary>
/// Ready means the database answers and holds every migration this build carries, so an instance deployed
/// ahead of its schema never takes traffic.
/// </summary>
internal sealed partial class DatabaseHealthCheck(PaymentsDbContext db, ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                LogUnreachable(logger);
                return HealthCheckResult.Unhealthy("database unreachable");
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            if (pending.Count > 0)
            {
                LogMigrationsPending(logger, pending.Count, pending[^1]);
                return HealthCheckResult.Unhealthy("migrations pending");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogCheckFailed(logger, exception);
            return HealthCheckResult.Unhealthy("database check failed");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Readiness: the database is unreachable")]
    private static partial void LogUnreachable(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Readiness: {PendingCount} migrations pending, newest {NewestMigration}")]
    private static partial void LogMigrationsPending(ILogger logger, int pendingCount, string newestMigration);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Readiness: the database check failed")]
    private static partial void LogCheckFailed(ILogger logger, Exception exception);
}
