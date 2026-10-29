using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Demo;
using PaymentPlatform.Application.Webhooks;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;
using PaymentPlatform.ServiceDefaults;

namespace PaymentPlatform.IntegrationTests.Webhooks;

/// <summary>ADR 003: every status change writes its event in the same transaction, and the dispatcher fans it out once.</summary>
public sealed class OutboxTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task Each_status_change_after_creation_writes_one_event_with_the_request_context()
    {
        var before = TimeProvider.System.GetUtcNow();
        var user = await Merchants.CreateUserAsync(Factory, "outbox-events@example.com");
        using var client = Clients.For(Factory, user);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "outbox-1");
        using var created = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242", reference: "ord-9"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Cancellation)).RootElement.GetProperty("id").GetGuid();
        using var captured = await PaymentRequests.MoveAsync(client, id, "capture");
        using var refunded = await PaymentRequests.MoveAsync(client, id, "refund");

        var messages = await MessagesForAsync(id);

        Assert.Equal(["payment.authorized", "payment.captured", "payment.refunded"], messages.Select(message => message.EventType));
        Assert.All(messages, message =>
        {
            Assert.Equal("outbox-1", message.CorrelationId);
            Assert.Matches("^00-[0-9a-f]{32}-[0-9a-f]{16}-0[01]$", message.TraceParent!);
            Assert.Null(message.DispatchedAt);
            Assert.InRange(message.OccurredAt, before, TimeProvider.System.GetUtcNow());
        });
        using var payload = JsonDocument.Parse(messages[1].Payload);
        Assert.Equal(messages[1].EventId, payload.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("payment.captured", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal(messages[1].OccurredAt, payload.RootElement.GetProperty("created_at").GetDateTimeOffset());
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(id, data.GetProperty("payment_id").GetGuid());
        Assert.Equal("captured", data.GetProperty("status").GetString());
        Assert.Equal("authorized", data.GetProperty("previous_status").GetString());
        Assert.Equal("ord-9", data.GetProperty("merchant_reference").GetString());
        // The card is not in the event at all: no card field, and neither the number nor its token anywhere. (A
        // search for "4242" alone also matched the digits of a timestamp's fraction and made this test flaky.)
        Assert.False(data.TryGetProperty("card", out _));
        Assert.DoesNotContain("4242424242424242", messages[1].Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("tok_", messages[1].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_change_that_does_not_save_writes_no_event()
    {
        // Two captures race; the loser's save fails on the row version, and its event goes with it.
        await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting("Acquirer:Simulated:LatencyMilliseconds", "1000"));
        var user = await Merchants.CreateUserAsync(factory, "outbox-race@example.com");
        using var client = Clients.For(factory, user);
        using var created = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Cancellation)).RootElement.GetProperty("id").GetGuid();

        var responses = await Task.WhenAll(
            PaymentRequests.MoveAsync(client, id, "capture", "outbox-race-a"),
            PaymentRequests.MoveAsync(client, id, "capture", "outbox-race-b"));
        foreach (var response in responses)
        {
            response.Dispose();
        }

        Assert.Single(await MessagesForAsync(id), message => message.EventType == "payment.captured");
    }

    [Fact]
    public async Task The_demo_seed_writes_history_but_no_events()
    {
        await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting(DemoSeed.SettingName, "true"));
        using var client = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.True(await db.PaymentEvents.AnyAsync(row => row.CorrelationId == DemoPayments.CorrelationId, Cancellation));
        Assert.False(await db.OutboxMessages.AnyAsync(message => message.MerchantId == DemoSeed.MerchantId, Cancellation));
    }

    [Fact]
    public async Task Dispatch_makes_one_delivery_per_event_for_an_enabled_endpoint_and_none_without_one()
    {
        var subscribed = await Merchants.CreateUserAsync(Factory, "subscribed@example.com");
        var silent = await Merchants.CreateUserAsync(Factory, "unsubscribed@example.com", merchantName: "Quiet");
        var disabled = await Merchants.CreateUserAsync(Factory, "disabled-endpoint@example.com", merchantName: "Paused");
        await AddEndpointAsync(subscribed, enabled: true);
        await AddEndpointAsync(disabled, enabled: false);
        foreach (var user in new[] { subscribed, silent, disabled })
        {
            await SeedEventsAsync(user, count: 3);
        }

        var dispatched = await DispatchAllAsync();

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.True(dispatched >= 9);
        Assert.False(await db.OutboxMessages.AnyAsync(message => message.DispatchedAt == null, Cancellation));
        Assert.Equal(3, await db.WebhookDeliveries.CountAsync(delivery => delivery.MerchantId == subscribed.MerchantId, Cancellation));
        Assert.Equal(0, await db.WebhookDeliveries.CountAsync(delivery => delivery.MerchantId == silent.MerchantId, Cancellation));
        Assert.Equal(0, await db.WebhookDeliveries.CountAsync(delivery => delivery.MerchantId == disabled.MerchantId, Cancellation));
        Assert.All(
            await db.WebhookDeliveries.Where(delivery => delivery.MerchantId == subscribed.MerchantId).ToListAsync(Cancellation),
            delivery => Assert.Equal((WebhookDeliveryStatus.Pending, 0), (delivery.Status, delivery.Attempts)));
    }

    [Fact]
    public async Task Dispatchers_running_at_once_never_take_the_same_message()
    {
        var user = await Merchants.CreateUserAsync(Factory, "competing@example.com");
        await AddEndpointAsync(user, enabled: true);
        await SeedEventsAsync(user, count: 250);

        // Four dispatchers drain the queue together; without the update locks two would read one message, fan it
        // out twice, and the unique index on (message, endpoint) would fail a save.
        var counts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DispatchAllAsync, Cancellation)));

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.Equal(250, await db.WebhookDeliveries.CountAsync(delivery => delivery.MerchantId == user.MerchantId, Cancellation));
        Assert.True(counts.Sum() >= 250);
        Assert.True(counts.Count(count => count > 0) > 1, "the work was shared, not done by one dispatcher");
    }

    [Fact]
    public async Task A_dispatcher_skips_messages_another_holds_instead_of_waiting_for_them()
    {
        var user = await Merchants.CreateUserAsync(Factory, "skip-locked@example.com");
        await AddEndpointAsync(user, enabled: true);
        await SeedEventsAsync(user, count: 30);

        // A stand-in for a dispatcher mid-batch: it holds the update locks on the oldest twenty messages, on a
        // connection of its own so the locks outlive the query.
        await using var holderScope = Factory.Services.CreateAsyncScope();
        await using var holder = new SqlConnection(holderScope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.GetConnectionString());
        await holder.OpenAsync(Cancellation);
        await using var held = await holder.BeginTransactionAsync(Cancellation);
        await using var claim = holder.CreateCommand();
        claim.Transaction = (SqlTransaction)held;
        claim.CommandText = """
            SELECT TOP (20) Id FROM payments.OutboxMessages WITH (UPDLOCK, ROWLOCK)
            WHERE DispatchedAt IS NULL AND MerchantId = @merchant
            ORDER BY Id
            """;
        claim.Parameters.AddWithValue("@merchant", user.MerchantId.Value);
        var locked = new List<long>();
        await using (var reader = await claim.ExecuteReaderAsync(Cancellation))
        {
            while (await reader.ReadAsync(Cancellation))
            {
                locked.Add(reader.GetInt64(0));
            }
        }

        Assert.Equal(20, locked.Count);

        // READPAST lets this one take the other ten at once; without it, it would queue behind the held locks.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DispatchOutboxMessages>().HandleAsync(timeout.Token);
        }

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var delivered = await db.WebhookDeliveries.Where(delivery => delivery.MerchantId == user.MerchantId)
                .Select(delivery => delivery.OutboxMessageId).ToListAsync(Cancellation);
            Assert.Equal(10, delivered.Count);
            Assert.Empty(delivered.Intersect(locked));
        }

        // The holder goes away without committing, as a crashed worker would, and its messages are free again.
        await held.RollbackAsync(Cancellation);
        await DispatchAllAsync();
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            Assert.Equal(30, await db.WebhookDeliveries.CountAsync(delivery => delivery.MerchantId == user.MerchantId, Cancellation));
        }
    }

    [Fact]
    public async Task The_dispatch_span_continues_the_trace_of_the_request_that_made_the_change()
    {
        var user = await Merchants.CreateUserAsync(Factory, "traced@example.com");
        await AddEndpointAsync(user, enabled: true);
        using var client = Clients.For(Factory, user);
        using var created = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242"));
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync(Cancellation)).RootElement.GetProperty("id").GetGuid();
        var message = Assert.Single(await MessagesForAsync(id));
        ActivityContext.TryParse(message.TraceParent, null, out var request);

        var spans = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                lock (spans)
                {
                    spans.Add(activity);
                }
            },
        };
        ActivitySource.AddActivityListener(listener);
        await DispatchAllAsync();

        Activity dispatch;
        lock (spans)
        {
            dispatch = Assert.Single(spans, span => span.OperationName == "outbox dispatch" && span.GetTagItem("payment.id") is Guid paymentId && paymentId == id);
        }

        Assert.Equal(request.TraceId, dispatch.TraceId);
        Assert.Equal(request.SpanId, dispatch.ParentSpanId);
        Assert.Equal(ActivityKind.Consumer, dispatch.Kind);
        Assert.Equal(message.CorrelationId, dispatch.GetTagItem(CorrelationIds.SpanTagName));
        Assert.Equal(message.EventId, dispatch.GetTagItem("messaging.message.id"));
    }

    private async Task<int> DispatchAllAsync()
    {
        var total = 0;
        int taken;
        do
        {
            await using var scope = Factory.Services.CreateAsyncScope();
            taken = await scope.ServiceProvider.GetRequiredService<DispatchOutboxMessages>().HandleAsync(Cancellation);
            total += taken;
        }
        while (taken > 0);

        return total;
    }

    private async Task<List<OutboxMessage>> MessagesForAsync(Guid paymentId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().OutboxMessages.AsNoTracking()
            .Where(message => message.PaymentId == new PaymentId(paymentId)).OrderBy(message => message.Id).ToListAsync(Cancellation);
    }

    private async Task AddEndpointAsync(TestUser user, bool enabled)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var endpoint = WebhookEndpoint.Create(Guid.NewGuid(), user.MerchantId, "https://hooks.example.com/payments", "whsec_test", TimeProvider.System.GetUtcNow());
        if (!enabled)
        {
            endpoint.Disable(TimeProvider.System.GetUtcNow());
        }

        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(Cancellation);
    }

    /// <summary>Authorized payments written straight through the domain, one outbox event each.</summary>
    private async Task SeedEventsAsync(TestUser user, int count)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var vault = scope.ServiceProvider.GetRequiredService<ICardVault>();
        var now = TimeProvider.System.GetUtcNow();
        for (var index = 0; index < count; index++)
        {
            var card = vault.Tokenize(new CardInput("4242424242424242", 12, now.Year + 2, "123")).Value;
            var payment = Payment.Create(
                PaymentId.New(now), user.MerchantId, Money.Create(100 + index, "USD").Value, card, null, Actor.MerchantUser(user.UserId.Value), now);
            payment.Authorize($"sim_auth_{index}", Actor.MerchantUser(user.UserId.Value), now);
            db.Payments.Add(payment);
            db.RecordChanges(payment, "seeded-events");
        }

        await db.SaveChangesAsync(Cancellation);
    }
}
