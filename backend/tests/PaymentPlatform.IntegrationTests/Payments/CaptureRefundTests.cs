using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Payments;

/// <summary><c>POST /api/v1/payments/{id}/capture</c> and <c>/refund</c>: the lifecycle, the acquirer, and idempotency together.</summary>
public sealed class CaptureRefundTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private static readonly int ExpiryYear = TimeProvider.System.GetUtcNow().Year + 3;

    [Fact]
    public async Task Capturing_an_authorized_payment_moves_it_and_writes_its_audit_row()
    {
        var user = await Merchants.CreateUserAsync(Factory, "capture@example.com");
        using var client = Clients.For(Factory, user);
        var id = await CreateAsync(client, "4242424242424242");
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "capture-1");

        using var response = await PaymentRequests.MoveAsync(client, id, "capture");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadAsync(response);
        Assert.Equal("captured", body.RootElement.GetProperty("status").GetString());
        var last = body.RootElement.GetProperty("history").EnumerateArray().Last();
        Assert.Equal("authorized", last.GetProperty("from").GetString());
        Assert.Equal("captured", last.GetProperty("to").GetString());
        Assert.Equal(user.UserId.ToString(), last.GetProperty("actor").GetProperty("id").GetString());
        Assert.Equal("capture-1", last.GetProperty("correlation_id").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task A_captured_payment_can_be_refunded_once()
    {
        var user = await Merchants.CreateUserAsync(Factory, "refund@example.com");
        using var client = Clients.For(Factory, user);
        var id = await CreateAsync(client, "5555555555554444");
        using var captured = await PaymentRequests.MoveAsync(client, id, "capture");

        using var refunded = await PaymentRequests.MoveAsync(client, id, "refund");
        using var again = await PaymentRequests.MoveAsync(client, id, "refund");

        Assert.Equal(HttpStatusCode.OK, refunded.StatusCode);
        using var body = await ReadAsync(refunded);
        Assert.Equal("refunded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("payment_not_refundable", await Problems.ReadCodeAsync(again));
    }

    [Fact]
    public async Task A_settled_payment_can_be_refunded()
    {
        var user = await Merchants.CreateUserAsync(Factory, "refund-settled@example.com");
        var id = await SeedAsync(user, payment =>
        {
            payment.Authorize(SimulatedAcquirer.AuthorizationPrefix + payment.Id.Value.ToString("N"), Actor.System("test"), Now());
            payment.Capture(Actor.System("test"), Now());
            payment.Settle(Actor.System("settlement"), Now());
        });
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.MoveAsync(client, id.Value, "refund");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = await ReadAsync(response);
        Assert.Equal("refunded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("settled", body.RootElement.GetProperty("history").EnumerateArray().Last().GetProperty("from").GetString());
    }

    [Theory]
    [InlineData("4000000000000119", "capture", "payment_not_capturable")]
    [InlineData("4000000000000002", "capture", "payment_not_capturable")]
    [InlineData("4242424242424242", "refund", "payment_not_refundable")]
    [InlineData("4000000000000002", "refund", "payment_not_refundable")]
    public async Task A_move_the_lifecycle_does_not_allow_is_a_409_and_changes_nothing(string card, string operation, string code)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"illegal-{operation}-{card[^4..]}@example.com");
        using var client = Clients.For(Factory, user);
        var id = await CreateAsync(client, card);
        var before = await StatusOfAsync(id);

        using var response = await PaymentRequests.MoveAsync(client, id, operation);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(code, await Problems.ReadCodeAsync(response));
        Assert.Equal(before, await StatusOfAsync(id));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_replays_and_a_new_key_is_refused_by_the_lifecycle()
    {
        var user = await Merchants.CreateUserAsync(Factory, "capture-retry@example.com");
        using var client = Clients.For(Factory, user);
        var id = await CreateAsync(client, "4242424242424242");

        using var first = await PaymentRequests.MoveAsync(client, id, "capture", "capture-once");
        using var replay = await PaymentRequests.MoveAsync(client, id, "capture", "capture-once");
        using var fresh = await PaymentRequests.MoveAsync(client, id, "capture", "capture-twice");
        using var reusedForRefund = await PaymentRequests.MoveAsync(client, id, "refund", "capture-once");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(await first.Content.ReadAsStringAsync(Cancellation), await replay.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(HttpStatusCode.Conflict, fresh.StatusCode);
        Assert.Equal("payment_not_capturable", await Problems.ReadCodeAsync(fresh));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, reusedForRefund.StatusCode);
        Assert.Equal("idempotency_key_reused", await Problems.ReadCodeAsync(reusedForRefund));
        Assert.Equal(1, await CountEventsAsync(id, PaymentStatus.Captured));
    }

    [Fact]
    public async Task Two_captures_racing_with_different_keys_capture_once()
    {
        // Both pass the state check while the slow card network answers; the payment's row version lets one save.
        await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting("Acquirer:Simulated:LatencyMilliseconds", "1000"));
        var user = await Merchants.CreateUserAsync(factory, "capture-race@example.com");
        using var client = Clients.For(factory, user);
        var id = await CreateAsync(client, "4242424242424242");

        var responses = await Task.WhenAll(
            PaymentRequests.MoveAsync(client, id, "capture", "race-a"),
            PaymentRequests.MoveAsync(client, id, "capture", "race-b"));

        try
        {
            Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.OK));
            var loser = Assert.Single(responses, response => response.StatusCode != HttpStatusCode.OK);
            Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
            Assert.Equal("payment_not_capturable", await Problems.ReadCodeAsync(loser));
            Assert.Equal(1, await CountEventsAsync(id, PaymentStatus.Captured, factory));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task When_the_acquirer_declines_a_capture_the_payment_is_unchanged()
    {
        var user = await Merchants.CreateUserAsync(Factory, "capture-declined@example.com");
        // An authorization the simulated acquirer does not know, so it declines the capture.
        var id = await SeedAsync(user, payment => payment.Authorize("sim_auth_unknown", Actor.System("test"), Now()));
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.MoveAsync(client, id.Value, "capture", "declined-capture");
        using var retry = await PaymentRequests.MoveAsync(client, id.Value, "capture", "declined-capture");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("capture_declined", await Problems.ReadCodeAsync(response));
        Assert.Equal(PaymentStatus.Authorized, await StatusOfAsync(id.Value));
        Assert.False(retry.Headers.Contains("Idempotent-Replayed"), "a refused request stores nothing");
    }

    [Theory]
    [InlineData("capture")]
    [InlineData("refund")]
    public async Task When_the_acquirer_does_not_answer_it_is_a_503_and_the_payment_is_unchanged(string operation)
    {
        await using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IAcquirer>(provider => new SilentFollowUps(ActivatorUtilities.CreateInstance<SimulatedAcquirer>(provider)))));
        var user = await Merchants.CreateUserAsync(factory, $"silent-{operation}@example.com");
        var id = await SeedAsync(user, payment =>
        {
            payment.Authorize(SimulatedAcquirer.AuthorizationPrefix + payment.Id.Value.ToString("N"), Actor.System("test"), Now());
            if (operation == "refund")
            {
                payment.Capture(Actor.System("test"), Now());
            }
        });
        using var client = Clients.For(factory, user);
        var before = await StatusOfAsync(id.Value);

        using var response = await PaymentRequests.MoveAsync(client, id.Value, operation);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("acquirer_unavailable", await Problems.ReadCodeAsync(response));
        Assert.Equal("5", response.Headers.GetValues("Retry-After").Single());
        Assert.Equal(before, await StatusOfAsync(id.Value));
    }

    [Fact]
    public async Task Another_merchants_payment_cannot_be_moved_and_looks_missing()
    {
        var owner = await Merchants.CreateUserAsync(Factory, "move-owner@example.com");
        var stranger = await Merchants.CreateUserAsync(Factory, "move-stranger@example.com", merchantName: "Elsewhere");
        using var ownerClient = Clients.For(Factory, owner);
        var id = await CreateAsync(ownerClient, "4242424242424242");
        using var strangerClient = Clients.For(Factory, stranger);

        using var capture = await PaymentRequests.MoveAsync(strangerClient, id, "capture");
        using var missing = await PaymentRequests.MoveAsync(strangerClient, Guid.NewGuid(), "capture");

        Assert.Equal(HttpStatusCode.NotFound, capture.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("not_found", await Problems.ReadCodeAsync(capture));
        Assert.Equal(PaymentStatus.Authorized, await StatusOfAsync(id));
    }

    [Fact]
    public async Task Moves_need_a_token_and_a_key()
    {
        var user = await Merchants.CreateUserAsync(Factory, "move-rules@example.com");
        using var client = Clients.For(Factory, user);
        var id = await CreateAsync(client, "4242424242424242");
        using var anonymous = Factory.CreateClient();

        using var noToken = await PaymentRequests.MoveAsync(anonymous, id, "capture");
        using var noKey = await client.PostAsync($"{PaymentRequests.Path}/{id}/capture", null, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal("idempotency_key_required", await Problems.ReadCodeAsync(noKey));
        Assert.Equal(PaymentStatus.Authorized, await StatusOfAsync(id));
    }

    private static DateTimeOffset Now() => TimeProvider.System.GetUtcNow();

    private static async Task<Guid> CreateAsync(HttpClient client, string card)
    {
        using var response = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body(card));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadAsync(response);
        return body.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<PaymentId> SeedAsync(TestUser user, Action<Payment> walk)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var card = scope.ServiceProvider.GetRequiredService<ICardVault>()
            .Tokenize(new CardInput("4242424242424242", 12, ExpiryYear, "123")).Value;
        var payment = Payment.Create(
            PaymentId.New(Now()), user.MerchantId, Money.Create(1_000, "USD").Value, card, null, Actor.MerchantUser(user.UserId.Value), Now());
        walk(payment);
        db.Payments.Add(payment);
        db.RecordChanges(payment, "seeded-by-test");
        await db.SaveChangesAsync(Cancellation);
        return payment.Id;
    }

    private async Task<PaymentStatus> StatusOfAsync(Guid id)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments
            .Where(payment => payment.Id == new PaymentId(id)).Select(payment => payment.Status).SingleAsync(Cancellation);
    }

    private async Task<int> CountEventsAsync(Guid id, PaymentStatus to, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? factory = null)
    {
        await using var scope = (factory ?? Factory).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().PaymentEvents
            .CountAsync(row => row.PaymentId == new PaymentId(id) && row.ToStatus == to, Cancellation);
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    /// <summary>The simulated acquirer, except that it never answers a capture or a refund.</summary>
    private sealed class SilentFollowUps(IAcquirer inner) : IAcquirer
    {
        public Task<AcquirerResponse> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken) =>
            inner.AuthorizeAsync(request, cancellationToken);

        public Task<AcquirerResponse> CaptureAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(AcquirerResponse.Unavailable());

        public Task<AcquirerResponse> RefundAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(AcquirerResponse.Unavailable());
    }
}
