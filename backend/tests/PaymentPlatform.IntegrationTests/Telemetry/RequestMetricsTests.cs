using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Telemetry;

/// <summary>
/// The RED signals (rate, errors, duration) as the OpenTelemetry pipeline exports them: the API's own meter
/// registration, read back through an in-memory exporter instead of OTLP.
/// </summary>
/// <remarks>
/// OpenTelemetry listens to a meter by name across the process, so another API host's
/// <c>Microsoft.AspNetCore.Hosting</c> meter, created or disposed while these tests run, could add to or interfere
/// with the points this host records (one run waited out its five seconds for points that never came). The class
/// therefore runs alone; counts stay lower bounds and absence is asserted by route.
/// </remarks>
[Collection(nameof(RequestMetricsTests))]
public sealed class RequestMetricsTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string RequestDuration = "http.server.request.duration";

    [Fact]
    public async Task Every_request_is_recorded_with_its_status_so_rate_errors_and_latency_follow()
    {
        var exported = new List<MetricSnapshot>();
        await using var factory = WithInMemoryMetrics(exported);
        using var client = factory.CreateClient();

        using var notFound = await client.GetAsync("/api/v1/payments/123", Cancellation);
        using var again = await client.GetAsync("/api/v1/payments/456", Cancellation);
        using var wrongMethod = await client.DeleteAsync("/health/live", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, wrongMethod.StatusCode);
        var points = await WaitForPointsAsync(
            factory, exported, points => CountWithStatus(points, 404) >= 2 && CountWithStatus(points, 405) >= 1);
        Assert.True(CountWithStatus(points, 404) >= 2, "both 404s are counted");
        Assert.True(CountWithStatus(points, 405) >= 1, "the 405 is counted");
        Assert.True(points.Where(point => point.Status == 404).Sum(point => point.Sum) > 0, "the 404s carry their durations");
    }

    [Fact]
    public async Task Health_probes_are_left_out_so_an_outage_is_not_counted_as_api_errors()
    {
        var exported = new List<MetricSnapshot>();
        await using var factory = WithInMemoryMetrics(exported);
        using var client = factory.CreateClient();

        for (var probe = 0; probe < 3; probe++)
        {
            using var live = await client.GetAsync("/health/live", Cancellation);
            using var ready = await client.GetAsync("/health/ready", Cancellation);
            Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
        }

        // A request that is recorded, so the export below is known to have caught up with the probes.
        using var marker = await client.GetAsync("/api/v1/payments/123", Cancellation);

        var points = await WaitForPointsAsync(factory, exported, points => CountWithStatus(points, 404) >= 1);
        Assert.DoesNotContain(points, point => point.Route?.StartsWith("/health", StringComparison.Ordinal) == true);
    }

    private WebApplicationFactory<Program> WithInMemoryMetrics(List<MetricSnapshot> exported) =>
        Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddInMemoryExporter(exported))));

    /// <summary>
    /// Hosting records a request's duration as the request ends, which can be just after the client has the
    /// response, so the export is flushed until the expected points arrive or five seconds pass.
    /// </summary>
    private static async Task<IReadOnlyList<RequestPoint>> WaitForPointsAsync(
        WebApplicationFactory<Program> factory,
        List<MetricSnapshot> exported,
        Func<IReadOnlyList<RequestPoint>, bool> done)
    {
        var meterProvider = factory.Services.GetRequiredService<MeterProvider>();
        var waited = Stopwatch.StartNew();
        IReadOnlyList<RequestPoint> points = [];
        while (true)
        {
            meterProvider.ForceFlush();
            points = Latest(exported);
            if (done(points) || waited.Elapsed > TimeSpan.FromSeconds(5))
            {
                return points;
            }

            await Task.Delay(50, Cancellation);
        }
    }

    private static List<RequestPoint> Latest(List<MetricSnapshot> exported)
    {
        var snapshot = exported.LastOrDefault(metric => metric.Name == RequestDuration);
        if (snapshot is null)
        {
            return [];
        }

        var points = new List<RequestPoint>();
        foreach (var point in snapshot.MetricPoints)
        {
            int? status = null;
            string? route = null;
            foreach (var tag in point.Tags)
            {
                switch (tag.Key)
                {
                    case "http.response.status_code":
                        status = Convert.ToInt32(tag.Value, CultureInfo.InvariantCulture);
                        break;
                    case "http.route":
                        route = tag.Value as string;
                        break;
                }
            }

            points.Add(new RequestPoint(status, route, point.GetHistogramCount(), point.GetHistogramSum()));
        }

        return points;
    }

    private static long CountWithStatus(IReadOnlyList<RequestPoint> points, int status) =>
        points.Where(point => point.Status == status).Sum(point => point.Count);

    private sealed record RequestPoint(int? Status, string? Route, long Count, double Sum);
}

/// <summary>Runs <see cref="RequestMetricsTests"/> with no other test class alongside it.</summary>
[CollectionDefinition(nameof(RequestMetricsTests), DisableParallelization = true)]
public sealed class RequestMetricsRunAlone;
