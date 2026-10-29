using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>
/// The real API, built from its own <c>Program</c>, pointed at a test database. Every middleware, filter, and
/// error mapping runs exactly as in production.
/// </summary>
public sealed class ApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>
    /// A connection string to a port nothing listens on, for the database-down paths. It keeps the client's
    /// default timeouts and retries, as the compose stack's does, so the probe's own deadline is what is tested.
    /// </summary>
    public const string UnreachableDatabase =
        "Server=127.0.0.1,1;Database=payments;User Id=sa;Password=unused;TrustServerCertificate=True";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Payments", connectionString);
        builder.UseSetting("Acquirer:Simulated:LatencyMilliseconds", "0");

        // Test output stays readable: only warnings and errors from the host reach the console.
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}
