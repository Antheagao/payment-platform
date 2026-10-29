using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PaymentPlatform.Persistence;

/// <summary>
/// Ready means the database answers and holds every migration this build carries, so an instance deployed
/// ahead of its schema never takes traffic.
/// </summary>
/// <remarks>
/// <para>
/// A probe asks once and answers inside <see cref="Deadline"/>, outage or not, so it runs on its own context
/// options (<see cref="PersistenceServiceCollectionExtensions.ConfigureProbe"/>): no transient-fault retries,
/// which would wait out the very outage the probe reports, and connect and command timeouts inside the deadline.
/// </para>
/// <para>
/// SqlClient does not honor the deadline everywhere: it resolves the host name synchronously inside
/// <c>OpenAsync</c>, blocking the caller for as long as the lookup takes (four seconds for a stopped container
/// under Docker). So the probe starts on a thread-pool thread, the check stops waiting at the deadline, and the
/// abandoned attempt finishes on its own.
/// </para>
/// <para>
/// It logs nothing itself: the health check service logs every unhealthy result once, at <c>Error</c>, with
/// the description and exception returned here. Neither reaches the HTTP response.
/// </para>
/// </remarks>
internal sealed class DatabaseHealthCheck(DbContextOptions<PaymentsDbContext> probeOptions) : IHealthCheck
{
    /// <summary>How long a probe may take; whoever asks (the dashboard waits five seconds) should wait longer.</summary>
    internal static readonly TimeSpan Deadline = TimeSpan.FromSeconds(3);

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.Run(() => ProbeAsync(cancellationToken), cancellationToken).WaitAsync(cancellationToken);

    private async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var db = new PaymentsDbContext(probeOptions);
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
