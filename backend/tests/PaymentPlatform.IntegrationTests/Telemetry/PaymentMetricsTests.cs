using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using PaymentPlatform.Application.Demo;
using PaymentPlatform.IntegrationTests.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Telemetry;

/// <summary>
/// <c>payments.transitions</c> (R19), read from the API host's own meter factory, so no other test's host is heard:
/// one count per saved status change, none for a change that was refused or for history loaded by the seed.
/// </summary>
public sealed class PaymentMetricsTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task Each_saved_status_change_is_counted_once_by_the_status_left_and_entered()
    {
        var user = await Merchants.CreateUserAsync(Factory, "counted@example.com");
        using var client = Clients.For(Factory, user);
        using var transitions = Collector();

        var approved = await CreateAsync(client, "4242424242424242");
        await CreateAsync(client, "4000000000000002");
        using (var captured = await PaymentRequests.MoveAsync(client, approved, "capture"))
        using (var refunded = await PaymentRequests.MoveAsync(client, approved, "refund"))
        {
            Assert.True(captured.IsSuccessStatusCode && refunded.IsSuccessStatusCode);
        }

        Assert.Equal(
            new Dictionary<string, long>
            {
                ["none->pending"] = 2,
                ["pending->authorized"] = 1,
                ["pending->failed"] = 1,
                ["authorized->captured"] = 1,
                ["captured->refunded"] = 1,
            },
            Totals(transitions));
    }

    [Fact]
    public async Task A_refused_move_and_a_replayed_request_count_nothing_more()
    {
        var user = await Merchants.CreateUserAsync(Factory, "refused@example.com");
        using var client = Clients.For(Factory, user);
        var declined = await CreateAsync(client, "4000000000000002");
        var approved = await CreateAsync(client, "4242424242424242");
        using var transitions = Collector();

        using (var refused = await PaymentRequests.MoveAsync(client, declined, "capture"))
        using (var first = await PaymentRequests.MoveAsync(client, approved, "capture", "capture-once"))
        using (var replay = await PaymentRequests.MoveAsync(client, approved, "capture", "capture-once"))
        {
            Assert.False(refused.IsSuccessStatusCode);
            Assert.True(replay.Headers.Contains("Idempotent-Replayed"));
        }

        Assert.Equal(new Dictionary<string, long> { ["authorized->captured"] = 1 }, Totals(transitions));
    }

    [Fact]
    public async Task The_demo_seeds_history_is_not_counted()
    {
        using var transitions = Collector();

        await DemoSeed.RunAsync(Factory.Services, Cancellation);

        Assert.Empty(transitions.GetMeasurementSnapshot());
    }

    private MetricCollector<long> Collector() =>
        new(Factory.Services.GetRequiredService<IMeterFactory>(), PaymentMetrics.MeterName, "payments.transitions");

    private static Dictionary<string, long> Totals(MetricCollector<long> collector) =>
        collector.GetMeasurementSnapshot()
            .GroupBy(measurement => $"{measurement.Tags[PaymentMetrics.FromTag]}->{measurement.Tags[PaymentMetrics.ToTag]}")
            .ToDictionary(group => group.Key, group => group.Sum(measurement => measurement.Value));

    private static async Task<Guid> CreateAsync(HttpClient client, string number)
    {
        using var response = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body(number));
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        return body.RootElement.GetProperty("id").GetGuid();
    }
}
