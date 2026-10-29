using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Startup;

public sealed class StartupTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task A_healthy_start_logs_nothing_at_warning_or_above()
    {
        // Started the way compose starts it, migrating at startup, so a warning here is one every demo shows.
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Database:MigrateOnStartup", "true");
            builder.ConfigureLogging(logging => logging.AddFakeLogging());
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/health/ready", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var warnings = factory.Services.GetFakeLogCollector().GetSnapshot()
            .Where(record => record.Level >= LogLevel.Warning)
            .Select(record => $"{record.Level} {record.Category}: {record.Message}");
        Assert.Empty(warnings);
    }
}
