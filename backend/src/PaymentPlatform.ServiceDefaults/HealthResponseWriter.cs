using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace PaymentPlatform.ServiceDefaults;

/// <summary>
/// Writes a health report as small JSON: overall status, each check's status, and the duration.
/// </summary>
/// <remarks>
/// Probes are unauthenticated, so the body never carries exception messages, connection strings, or
/// descriptions; the failing check's cause is in the logs.
/// </remarks>
public static class HealthResponseWriter
{
    public static async Task WriteAsync(HttpContext context, HealthReport report)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(report);

        context.Response.ContentType = "application/json";
        await using var json = new Utf8JsonWriter(context.Response.Body);
        json.WriteStartObject();
        json.WriteString("status", Describe(report.Status));
        json.WriteNumber("duration_ms", Math.Round(report.TotalDuration.TotalMilliseconds, 1));
        json.WriteStartObject("checks");
        foreach (var (name, entry) in report.Entries.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            json.WriteString(name, Describe(entry.Status));
        }

        json.WriteEndObject();
        json.WriteEndObject();
        await json.FlushAsync(context.RequestAborted);
    }

    private static string Describe(HealthStatus status) => status switch
    {
        HealthStatus.Healthy => "healthy",
        HealthStatus.Degraded => "degraded",
        HealthStatus.Unhealthy => "unhealthy",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "unknown health status"),
    };
}
