using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Health;

public sealed class DatabaseHealthCheckTests
{
    // The timeout turns a regression into a failure instead of a run that never ends.
    [Fact(Timeout = 10_000)]
    public async Task Gives_up_at_its_deadline_when_the_connection_ignores_cancellation()
    {
        using var stall = new StallingInterceptor();
        var options = new DbContextOptionsBuilder<PaymentsDbContext>();
        PersistenceServiceCollectionExtensions.ConfigureProbe(options, "Server=127.0.0.1,1;Database=payments;TrustServerCertificate=True");
        options.AddInterceptors(stall);
        var check = new DatabaseHealthCheck(options.Options);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(200));
        var stopwatch = Stopwatch.StartNew();

        try
        {
            // The health check service turns this into an unhealthy "timeout" entry.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => check.CheckHealthAsync(new HealthCheckContext(), deadline.Token));
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"the check took {stopwatch.Elapsed}");
        }
        finally
        {
            stall.Release();
        }
    }

    /// <summary>
    /// Blocks the calling thread inside the asynchronous open and ignores the cancellation token, as SqlClient
    /// does while it resolves a host name that no longer exists.
    /// </summary>
    private sealed class StallingInterceptor : DbConnectionInterceptor, IDisposable
    {
        private readonly ManualResetEventSlim _released = new();

        public void Release() => _released.Set();

        public void Dispose() => _released.Dispose();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            _released.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
            return ValueTask.FromResult(result);
        }
    }
}
