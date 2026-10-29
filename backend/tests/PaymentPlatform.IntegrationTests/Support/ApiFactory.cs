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

    /// <summary>
    /// A fixed test key, so a test can forge tokens the API should accept or refuse. It is 64 bytes, longer than
    /// HS256 needs, so a test can also sign with HS512 and the same key to prove the algorithm is pinned.
    /// </summary>
    public static readonly byte[] SigningKey = Enumerable.Range(1, 64).Select(value => (byte)value).ToArray();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Payments", connectionString);
        builder.UseSetting("Acquirer:Simulated:LatencyMilliseconds", "0");
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(SigningKey));

        // Every test logs in from the same in-memory address; the limits are tested with their own settings.
        builder.UseSetting("Auth:LoginRateLimit:PerAddressPermits", "10000");
        builder.UseSetting("Auth:LoginRateLimit:PerEmailPermits", "10000");

        // Test output stays readable: only warnings and errors from the host reach the console.
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }
}
