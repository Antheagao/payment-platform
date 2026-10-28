using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace PaymentPlatform.ServiceDefaults;

/// <summary>The wiring every host shares: JSON logs, OpenTelemetry, health checks, and correlation ids (ADR 005).</summary>
public static class ServiceDefaultsExtensions
{
    /// <summary>Every <c>ActivitySource</c> and <c>Meter</c> the platform creates is named under this prefix.</summary>
    public const string TelemetryPrefix = "PaymentPlatform";

    public const string LivenessPath = "/health/live";

    public const string ReadinessPath = "/health/ready";

    /// <summary>Health checks with this tag run on the liveness probe; every check runs on readiness.</summary>
    public const string LiveTag = "live";

    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureLogging();
        builder.ConfigureOpenTelemetry();
        builder.Services.AddHealthChecks().AddCheck("self", () => HealthCheckResult.Healthy(), [LiveTag]);
        return builder;
    }

    /// <summary>Assigns the correlation id first, so every later log line and error response carries it.</summary>
    public static IApplicationBuilder UseCorrelationIds(this IApplicationBuilder app) =>
        app.UseMiddleware<CorrelationIdMiddleware>();

    /// <summary>
    /// Maps <c>/health/live</c> (the process answers) and <c>/health/ready</c> (every dependency answers),
    /// anonymous and outside the versioned API, for load balancers and orchestrators.
    /// </summary>
    public static IEndpointRouteBuilder MapDefaultEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // MapHealthChecks answers every method; probes only GET or HEAD, so anything else is a 405.
        string[] probeMethods = [HttpMethods.Get, HttpMethods.Head];
        endpoints.MapHealthChecks(LivenessPath, new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains(LiveTag),
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).WithMetadata(new HttpMethodMetadata(probeMethods));
        endpoints.MapHealthChecks(ReadinessPath, new HealthCheckOptions
        {
            ResponseWriter = HealthResponseWriter.WriteAsync,
        }).WithMetadata(new HttpMethodMetadata(probeMethods));
        return endpoints;
    }

    private static void ConfigureLogging(this IHostApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.Configure(options => options.ActivityTrackingOptions =
            ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });
        builder.Logging.AddOpenTelemetry(options =>
        {
            options.IncludeScopes = true;
            options.IncludeFormattedMessage = true;
        });
    }

    private static void ConfigureOpenTelemetry(this IHostApplicationBuilder builder)
    {
        var telemetry = builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceNamespace: "payment-platform"))
            .WithTracing(tracing => tracing
                .AddSource($"{TelemetryPrefix}.*")
                .AddAspNetCoreInstrumentation(options => options.Filter = IsNotHealthProbe)
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation())
            .WithMetrics(metrics => metrics
                .AddMeter($"{TelemetryPrefix}.*")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation());

        // Export only where a collector is configured (the Aspire dashboard in compose, Azure Monitor in
        // production); tests and a bare `dotnet run` keep telemetry in-process.
        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
        {
            telemetry.UseOtlpExporter();
        }
    }

    // Probes run every few seconds; tracing them would bury the requests worth looking at.
    private static bool IsNotHealthProbe(HttpContext context) =>
        !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase);
}
