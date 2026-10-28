using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PaymentPlatform.Persistence;

/// <summary>
/// Ready means the database answers and holds every migration this build carries, so an instance deployed
/// ahead of its schema never takes traffic.
/// </summary>
/// <remarks>
/// It logs nothing itself: the health check service logs every unhealthy result once, at <c>Error</c>, with
/// the description and exception returned here. Neither reaches the HTTP response.
/// </remarks>
internal sealed class DatabaseHealthCheck(PaymentsDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                return HealthCheckResult.Unhealthy("database unreachable");
            }

            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{pending.Count} migrations pending, newest {pending[^1]}");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("database check failed", exception);
        }
    }
}
