using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Webhooks;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Staff;

/// <summary>
/// The dead-letter replay (ADR 003, ADR 011): staff list every merchant's dead-lettered deliveries and put one back on
/// the queue, which the worker's next run delivers; nothing else can be replayed.
/// </summary>
public sealed class DeadLetterReplayTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string ListPath = "/api/v1/staff/webhook-deliveries?status=dead_lettered";

    // Starts at the real time: the staff token comes from the API on this clock, and JwtBearer checks it on the real one.
    private readonly FakeTimeProvider _clock = new(TimeProvider.System.GetUtcNow());
    private readonly AnsweringReceiver _receiver = new();
    private WebApplicationFactory<Program>? _api;

    private WebApplicationFactory<Program> Api => _api ??= Factory.WithWebHostBuilder(builder =>
    {
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton<IWebhookSender>(_receiver);
        });
    });

    [Fact]
    public async Task Staff_see_every_merchants_dead_letters_newest_first_a_page_at_a_time_by_host_only()
    {
        var first = await DeadLettersAsync("dead-one@example.com", count: 2);
        var second = await DeadLettersAsync("dead-two@example.com", count: 1);
        using var client = await StaffAsync("lister@example.com");

        using var pageOne = await client.GetAsync($"{ListPath}&limit=2", Cancellation);
        using var bodyOne = JsonDocument.Parse(await pageOne.Content.ReadAsStringAsync(Cancellation));
        var cursor = bodyOne.RootElement.GetProperty("next_cursor").GetString();
        using var pageTwo = await client.GetAsync($"{ListPath}&limit=2&cursor={cursor}", Cancellation);
        using var bodyTwo = JsonDocument.Parse(await pageTwo.Content.ReadAsStringAsync(Cancellation));

        var ids = bodyOne.RootElement.GetProperty("data").EnumerateArray()
            .Concat(bodyTwo.RootElement.GetProperty("data").EnumerateArray())
            .Select(row => row.GetProperty("id").GetInt64()).ToList();
        Assert.Equal(first.Concat(second).OrderDescending(), ids);
        Assert.Equal(JsonValueKind.Null, bodyTwo.RootElement.GetProperty("next_cursor").ValueKind);
        var row = bodyOne.RootElement.GetProperty("data")[0];
        Assert.Equal(("payment.authorized", 12, 500, FakeHostResolver.PublicHost), (
            row.GetProperty("event_type").GetString(),
            row.GetProperty("attempts").GetInt32(),
            row.GetProperty("last_status_code").GetInt32(),
            row.GetProperty("endpoint_host").GetString()));
        // The merchant's URL carries a token in its query; the list never shows more than the host.
        Assert.DoesNotContain("token=", bodyOne.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "status", "status_required")]
    [InlineData("?status=pending", "status", "invalid_status")]
    [InlineData("?status=dead_lettered&limit=0", "limit", "invalid_limit")]
    [InlineData("?status=dead_lettered&cursor=not-a-cursor!", "cursor", "invalid_cursor")]
    public async Task A_list_request_that_does_not_parse_gets_its_field_code(string query, string field, string code)
    {
        using var client = await StaffAsync("bad-query@example.com");

        using var response = await client.GetAsync($"/api/v1/staff/webhook-deliveries{query}", Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await Problems.ReadAsync(response);
        Assert.Equal(code, problem.RootElement.GetProperty("errors").GetProperty(field)[0].GetString());
    }

    [Fact]
    public async Task A_replay_puts_the_delivery_back_on_the_queue_and_the_worker_delivers_it()
    {
        var id = Assert.Single(await DeadLettersAsync("replayed@example.com", count: 1));
        using var client = await StaffAsync("replayer@example.com");
        using var replays = new MetricCollector<long>(Api.Services.GetRequiredService<IMeterFactory>(), QueueMetrics.MeterName, "webhook.delivery.replays");

        using var response = await client.PostAsync($"/api/v1/staff/webhook-deliveries/{id}/replay", null, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(("pending", 0), (body.RootElement.GetProperty("status").GetString(), body.RootElement.GetProperty("attempts").GetInt32()));
        Assert.Equal(1, replays.GetMeasurementSnapshot().Sum(measurement => measurement.Value));

        _receiver.Status = 204;
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DeliverWebhooks>().HandleAsync(Cancellation);
        }

        var delivery = await DeliveryAsync(id);
        Assert.Equal((WebhookDeliveryStatus.Delivered, 1, 204), (delivery.Status, delivery.Attempts, delivery.LastStatusCode));
        Assert.Contains(Api.Services.GetFakeLogCollector().GetSnapshot(), record =>
            record.Message.Contains($"Webhook delivery {id}", StringComparison.Ordinal) && record.Message.Contains("replayed by staff user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Only_a_dead_letter_to_an_enabled_endpoint_can_be_replayed()
    {
        var replayed = Assert.Single(await DeadLettersAsync("twice@example.com", count: 1));
        var disabled = Assert.Single(await DeadLettersAsync("disabled@example.com", count: 1, disableEndpoint: true));
        using var client = await StaffAsync("careful@example.com");

        using var first = await client.PostAsync($"/api/v1/staff/webhook-deliveries/{replayed}/replay", null, Cancellation);
        using var again = await client.PostAsync($"/api/v1/staff/webhook-deliveries/{replayed}/replay", null, Cancellation);
        using var toDisabled = await client.PostAsync($"/api/v1/staff/webhook-deliveries/{disabled}/replay", null, Cancellation);
        using var missing = await client.PostAsync("/api/v1/staff/webhook-deliveries/999999/replay", null, Cancellation);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal((HttpStatusCode.Conflict, "delivery_not_dead_lettered"), (again.StatusCode, await Problems.ReadCodeAsync(again)));
        Assert.Equal((HttpStatusCode.Conflict, "webhook_endpoint_disabled"), (toDisabled.StatusCode, await Problems.ReadCodeAsync(toDisabled)));
        Assert.Equal((HttpStatusCode.NotFound, "not_found"), (missing.StatusCode, await Problems.ReadCodeAsync(missing)));
        Assert.Equal(WebhookDeliveryStatus.DeadLettered, (await DeliveryAsync(disabled)).Status);
    }

    private async Task<HttpClient> StaffAsync(string email)
    {
        await StaffAuthTests.CreateStaffAsync(Api, email);
        return await StaffAuthTests.StaffClientAsync(Api, email);
    }

    /// <summary>A merchant with an endpoint, and <paramref name="count"/> deliveries that used up every attempt.</summary>
    private async Task<List<long>> DeadLettersAsync(string email, int count, bool disableEndpoint = false)
    {
        var user = await Merchants.CreateUserAsync(Api, email, merchantName: email);
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var vault = scope.ServiceProvider.GetRequiredService<ICardVault>();
            var now = _clock.GetUtcNow();
            db.WebhookEndpoints.Add(WebhookEndpoint.Create(
                Guid.NewGuid(), user.MerchantId, $"https://{FakeHostResolver.PublicHost}/hooks?token=s3cret", WebhookSecrets.New(), now));
            for (var index = 0; index < count; index++)
            {
                var card = vault.Tokenize(new CardInput("4242424242424242", 12, now.Year + 2, "123")).Value;
                var payment = Payment.Create(PaymentId.New(now), user.MerchantId, Money.Create(100 + index, "USD").Value, card, null, Actor.MerchantUser(user.UserId.Value), now);
                payment.Authorize($"sim_auth_{index}", Actor.MerchantUser(user.UserId.Value), now);
                db.Payments.Add(payment);
                db.RecordChanges(payment, "dead-letters");
            }

            await db.SaveChangesAsync(Cancellation);
        }

        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var dispatch = scope.ServiceProvider.GetRequiredService<DispatchOutboxMessages>();
            while (await dispatch.HandleAsync(Cancellation) > 0)
            {
            }
        }

        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var deliveries = await db.WebhookDeliveries.Where(delivery => delivery.MerchantId == user.MerchantId).OrderBy(delivery => delivery.Id).ToListAsync(Cancellation);
            foreach (var delivery in deliveries)
            {
                for (var attempt = 1; attempt < WebhookRetrySchedule.MaxAttempts; attempt++)
                {
                    delivery.Failed(500, "HTTP 500", _clock.GetUtcNow());
                }

                delivery.Failed(500, "HTTP 500", nextAttemptAt: null);
            }

            if (disableEndpoint)
            {
                var endpoint = await db.WebhookEndpoints.SingleAsync(row => row.MerchantId == user.MerchantId, Cancellation);
                endpoint.Update(endpoint.Url, enabled: false, _clock.GetUtcNow());
            }

            await db.SaveChangesAsync(Cancellation);
            return deliveries.Select(delivery => delivery.Id).ToList();
        }
    }

    private async Task<WebhookDelivery> DeliveryAsync(long id)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().WebhookDeliveries.AsNoTracking().SingleAsync(row => row.Id == id, Cancellation);
    }

    /// <summary>The merchant's receiver, answering every delivery with <see cref="Status"/>.</summary>
    private sealed class AnsweringReceiver : IWebhookSender
    {
        public int Status { get; set; } = 500;

        public Task<WebhookAttempt> SendAsync(WebhookRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new WebhookAttempt(Status, null));
    }
}
