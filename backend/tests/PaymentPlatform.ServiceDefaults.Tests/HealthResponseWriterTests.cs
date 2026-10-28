using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PaymentPlatform.ServiceDefaults.Tests;

public sealed class HealthResponseWriterTests
{
    [Fact]
    public async Task The_report_lists_each_check_without_leaking_its_cause()
    {
        var entries = new Dictionary<string, HealthReportEntry>
        {
            ["self"] = new(HealthStatus.Healthy, null, TimeSpan.FromMilliseconds(1), null, null),
            ["database"] = new(
                HealthStatus.Unhealthy,
                "Server=sql;Password=hunter2",
                TimeSpan.FromMilliseconds(40),
                new InvalidOperationException("login failed for user 'sa'"),
                null),
        };
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthResponseWriter.WriteAsync(context, new HealthReport(entries, TimeSpan.FromMilliseconds(41)));

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync(TestContext.Current.CancellationToken);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("application/json", context.Response.ContentType);
        Assert.Equal("unhealthy", json.RootElement.GetProperty("status").GetString());
        Assert.Equal("healthy", json.RootElement.GetProperty("checks").GetProperty("self").GetString());
        Assert.Equal("unhealthy", json.RootElement.GetProperty("checks").GetProperty("database").GetString());
        Assert.DoesNotContain("hunter2", body, StringComparison.Ordinal);
        Assert.DoesNotContain("login failed", body, StringComparison.Ordinal);
    }
}
