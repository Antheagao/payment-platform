using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using PaymentPlatform.Application;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Settlement;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Settlement;

/// <summary>
/// <c>payments.SettleCapturedPayments</c> through the <c>SettlePayments</c> use case (ADR 003): what it settles, what it
/// writes, that it agrees with the C# domain and outbox, and that it runs safely beside other workers and requests.
/// </summary>
public sealed class SettlementTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private static readonly TimeSpan Delay = TimeSpan.FromDays(1);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
    private WebApplicationFactory<Program>? _api;

    private WebApplicationFactory<Program> Api => _api ??= Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Settlement:Delay", Delay.ToString());
        builder.UseSetting("Settlement:BatchSize", "500");
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(_clock));
    });

    [Fact]
    public async Task Only_payments_captured_before_the_delay_settle_and_the_totals_add_up()
    {
        var user = await Merchants.CreateUserAsync(Api, "settle-due@example.com");
        var due = await SeedAsync(user, PaymentStatus.Captured, count: 3, amount: 1_000, currency: "USD", age: Delay + TimeSpan.FromMinutes(1));
        var dueEuro = await SeedAsync(user, PaymentStatus.Captured, count: 2, amount: 500, currency: "EUR", age: Delay + TimeSpan.FromHours(5));
        var recent = await SeedAsync(user, PaymentStatus.Captured, count: 2, amount: 700, currency: "USD", age: Delay - TimeSpan.FromMinutes(1));
        var others = new List<PaymentId>();
        foreach (var status in new[] { PaymentStatus.Pending, PaymentStatus.Authorized, PaymentStatus.Failed, PaymentStatus.Refunded, PaymentStatus.Settled })
        {
            others.AddRange(await SeedAsync(user, status, count: 1, amount: 900, currency: "USD", age: Delay * 3));
        }

        var totals = await SettleAsync();

        // Amounts alternate between the base and one more: 1,000 + 1,001 + 1,000 dollars' cents, 500 + 501 cents.
        Assert.Equal([("EUR", 2, 1_001L), ("USD", 3, 3_001L)], totals.OrderBy(total => total.Currency).Select(total => (total.Currency, total.Payments, total.AmountMinor)));
        var statuses = await StatusesAsync(user);
        Assert.All(due.Concat(dueEuro), id => Assert.Equal(PaymentStatus.Settled, statuses[id]));
        Assert.All(recent, id => Assert.Equal(PaymentStatus.Captured, statuses[id]));
        Assert.Equal(
            [PaymentStatus.Pending, PaymentStatus.Authorized, PaymentStatus.Failed, PaymentStatus.Refunded, PaymentStatus.Settled],
            others.Select(id => statuses[id]));
    }

    [Fact]
    public async Task Each_settled_payment_gets_the_audit_row_and_event_the_domain_would_write()
    {
        var user = await Merchants.CreateUserAsync(Api, "settle-agrees@example.com");
        var id = Assert.Single(await SeedAsync(user, PaymentStatus.Captured, count: 1, amount: 2_599, currency: "USD", age: Delay * 2, reference: "INV-42"));
        Payment before;
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            before = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking().SingleAsync(payment => payment.Id == id, Cancellation);
        }

        using var run = new Activity("job settle-payments").SetIdFormat(ActivityIdFormat.W3C);
        run.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        run.Start();
        await SettleAsync();
        run.Stop();

        // What the C# domain and outbox would have written for the same move at the same instant.
        var now = _clock.GetUtcNow();
        Assert.True(before.Settle(Actor.System("settlement"), now).IsSuccess);
        var change = Assert.Single(before.DequeueEvents());
        var expectedRow = PaymentEvent.From(change, "ignored");
        var expectedEvent = OutboxMessage.For(before, change, "ignored", now)!;

        await using var check = Api.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var settled = await db.Payments.AsNoTracking().SingleAsync(payment => payment.Id == id, Cancellation);
        Assert.Equal((PaymentStatus.Settled, now), (settled.Status, settled.UpdatedAt));
        var row = await db.PaymentEvents.AsNoTracking().Where(candidate => candidate.PaymentId == id).OrderBy(candidate => candidate.Id).LastAsync(Cancellation);
        Assert.Equal(
            (expectedRow.FromStatus, expectedRow.ToStatus, expectedRow.ActorKind, expectedRow.ActorId, expectedRow.OccurredAt, expectedRow.Reason),
            (row.FromStatus, row.ToStatus, row.ActorKind, row.ActorId, row.OccurredAt, row.Reason));
        Assert.Matches("^settlement-[0-9a-f]{32}$", row.CorrelationId!);

        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(candidate => candidate.PaymentId == id, Cancellation);
        Assert.Equal((expectedEvent.EventType, expectedEvent.OccurredAt, row.CorrelationId), (message.EventType, message.OccurredAt, message.CorrelationId));
        Assert.Equal(run.Id, message.TraceParent);
        Assert.Null(message.DispatchedAt);
        using var actual = JsonDocument.Parse(message.Payload);
        using var expected = JsonDocument.Parse(expectedEvent.Payload);
        Assert.Equal(message.EventId.ToString(), actual.RootElement.GetProperty("id").GetString());
        Assert.Equal(Fields(expected.RootElement), Fields(actual.RootElement));
    }

    [Fact]
    public async Task Running_again_settles_nothing_twice()
    {
        var user = await Merchants.CreateUserAsync(Api, "settle-twice@example.com");
        var ids = await SeedAsync(user, PaymentStatus.Captured, count: 4, amount: 100, currency: "USD", age: Delay * 2);

        Assert.Equal(4, SettlePayments.CountOf(await SettleAsync()));
        Assert.Empty(await SettleAsync());
        _clock.Advance(Delay * 2);
        Assert.Empty(await SettleAsync());

        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        foreach (var id in ids)
        {
            Assert.Equal(1, await db.PaymentEvents.CountAsync(row => row.PaymentId == id && row.ToStatus == PaymentStatus.Settled, Cancellation));
            Assert.Equal(1, await db.OutboxMessages.CountAsync(message => message.PaymentId == id && message.EventType == "payment.settled", Cancellation));
        }
    }

    [Fact]
    public async Task Settlers_running_at_once_settle_each_payment_once()
    {
        var user = await Merchants.CreateUserAsync(Api, "settle-shared@example.com");
        await SeedAsync(user, PaymentStatus.Captured, count: 1_200, amount: 100, currency: "USD", age: Delay * 2);

        var settled = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(SettleAllAsync, Cancellation)));

        Assert.Equal(1_200, settled.Sum());
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.Equal(1_200, await db.PaymentEvents.CountAsync(row => row.MerchantId == user.MerchantId && row.ToStatus == PaymentStatus.Settled, Cancellation));
        Assert.False(await db.Payments.AnyAsync(payment => payment.MerchantId == user.MerchantId && payment.Status == PaymentStatus.Captured, Cancellation));
    }

    [Fact]
    public async Task A_settler_skips_payments_another_holds_instead_of_waiting_for_them()
    {
        var user = await Merchants.CreateUserAsync(Api, "settle-held@example.com");
        await SeedAsync(user, PaymentStatus.Captured, count: 30, amount: 100, currency: "USD", age: Delay * 2);

        // A refund or another settler mid-update: it holds the update locks on twenty of the captured payments.
        await using var holderScope = Api.Services.CreateAsyncScope();
        await using var holder = new SqlConnection(holderScope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.GetConnectionString());
        await holder.OpenAsync(Cancellation);
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync(Cancellation);
        await using (var claim = new SqlCommand(
            "SELECT TOP (20) Id FROM payments.Payments WITH (UPDLOCK, ROWLOCK) WHERE MerchantId = @merchant AND Status = 'Captured' ORDER BY UpdatedAt", holder, held))
        {
            claim.Parameters.AddWithValue("@merchant", user.MerchantId.Value);
            await using var reader = await claim.ExecuteReaderAsync(Cancellation);
            while (await reader.ReadAsync(Cancellation))
            {
            }
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            Assert.Equal(10, SettlePayments.CountOf(await scope.ServiceProvider.GetRequiredService<SettlePayments>().HandleAsync(timeout.Token)));
        }

        await held.RollbackAsync(Cancellation);
        Assert.Equal(20, await SettleAllAsync());
    }

    [Fact]
    public async Task A_refund_racing_settlement_refunds_the_settled_payment()
    {
        // The payment settles while the refund waits on the acquirer: the refund was approved, so it must be
        // recorded, against the payment's new state. The acquirer's latency runs on the fake clock, so the test
        // decides when it answers: after the refund has read the payment and the settlement has changed it.
        await using var slow = Api.WithWebHostBuilder(builder => builder.UseSetting("Acquirer:Simulated:LatencyMilliseconds", "1000"));
        var user = await Merchants.CreateUserAsync(slow, "settle-race@example.com");
        var id = Assert.Single(await SeedAsync(user, PaymentStatus.Captured, count: 1, amount: 4_200, currency: "USD", age: Delay * 2, factory: slow));
        using var client = Clients.For(slow, user);
        var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (activity.OperationName == "acquirer refund" && Equals(activity.GetTagItem("payment.id"), id.Value))
                {
                    waiting.TrySetResult();
                }
            },
        };
        ActivitySource.AddActivityListener(listener);

        var refund = PaymentRequests.MoveAsync(client, id.Value, "refund");
        await waiting.Task.WaitAsync(TimeSpan.FromSeconds(10), Cancellation);
        Assert.Equal(1, SettlePayments.CountOf(await SettleAsync(slow)));
        _clock.Advance(TimeSpan.FromSeconds(1));
        using var response = await refund;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("refunded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            ["pending", "authorized", "captured", "settled", "refunded"],
            body.RootElement.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("to").GetString()));
    }

    [Fact]
    public async Task A_settled_payment_can_still_be_refunded()
    {
        var user = await Merchants.CreateUserAsync(Api, "settle-then-refund@example.com");
        var id = Assert.Single(await SeedAsync(user, PaymentStatus.Captured, count: 1, amount: 300, currency: "USD", age: Delay * 2));
        await SettleAsync();
        using var client = Clients.For(Api, user);

        using var response = await PaymentRequests.MoveAsync(client, id.Value, "refund");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<IReadOnlyList<SettlementTotal>> SettleAsync(WebApplicationFactory<Program>? factory = null)
    {
        await using var scope = (factory ?? Api).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SettlePayments>().HandleAsync(Cancellation);
    }

    private async Task<int> SettleAllAsync()
    {
        var total = 0;
        int settled;
        do
        {
            settled = SettlePayments.CountOf(await SettleAsync());
            total += settled;
        }
        while (settled > 0);

        return total;
    }

    /// <summary>Payments walked through the domain to <paramref name="status"/>, their last change <paramref name="age"/> ago.</summary>
    private async Task<List<PaymentId>> SeedAsync(
        TestUser user,
        PaymentStatus status,
        int count,
        long amount,
        string currency,
        TimeSpan age,
        string? reference = null,
        WebApplicationFactory<Program>? factory = null)
    {
        await using var scope = (factory ?? Api).Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var vault = scope.ServiceProvider.GetRequiredService<ICardVault>();
        var at = _clock.GetUtcNow() - age;
        var actor = Actor.MerchantUser(user.UserId.Value);
        var ids = new List<PaymentId>();
        for (var index = 0; index < count; index++)
        {
            var card = vault.Tokenize(new CardInput("4242424242424242", 12, at.Year + 2, "123")).Value;
            var payment = Payment.Create(PaymentId.New(at), user.MerchantId, Money.Create(amount + index % 2, currency).Value, card, reference, actor, at);
            if (status != PaymentStatus.Pending)
            {
                Expect(status == PaymentStatus.Failed ? payment.Fail("card_declined", actor, at) : payment.Authorize(SimulatedAcquirer.AuthorizationPrefix + payment.Id.Value.ToString("N"), actor, at));
            }

            if (status is PaymentStatus.Captured or PaymentStatus.Settled or PaymentStatus.Refunded)
            {
                Expect(payment.Capture(actor, at));
            }

            if (status == PaymentStatus.Settled)
            {
                Expect(payment.Settle(Actor.System("settlement"), at));
            }

            if (status == PaymentStatus.Refunded)
            {
                Expect(payment.Refund(actor, at));
            }

            db.Payments.Add(payment);
            db.RecordChanges(payment, "seeded-settlement", publish: false);
            ids.Add(payment.Id);
        }

        await db.SaveChangesAsync(Cancellation);
        return ids;
    }

    private async Task<Dictionary<PaymentId, PaymentStatus>> StatusesAsync(TestUser user)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking()
            .Where(payment => payment.MerchantId == user.MerchantId)
            .ToDictionaryAsync(payment => payment.Id, payment => payment.Status, Cancellation);
    }

    private static void Expect(Result result) => Assert.True(result.IsSuccess, result.Error?.Code);

    /// <summary>Every field but the event id, with timestamps compared as instants rather than text.</summary>
    private static Dictionary<string, string?> Fields(JsonElement root)
    {
        var fields = new Dictionary<string, string?>
        {
            ["type"] = root.GetProperty("type").GetString(),
            ["created_at"] = root.GetProperty("created_at").GetDateTimeOffset().ToString("O"),
        };
        foreach (var field in root.GetProperty("data").EnumerateObject())
        {
            fields[$"data.{field.Name}"] = field.Value.ValueKind == JsonValueKind.Null ? null : field.Value.ToString();
        }

        return fields;
    }
}
