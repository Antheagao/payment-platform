using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Idempotency;
using PaymentPlatform.Application.Payments;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Payments;

/// <summary>ADR 007 on <c>POST /api/v1/payments</c>: a retry never charges twice.</summary>
public sealed class IdempotencyTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private static readonly int ExpiryYear = TimeProvider.System.GetUtcNow().Year + 3;
    private static readonly JsonSerializerOptions SnakeCase = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    [Fact]
    public async Task A_create_without_a_key_is_refused_and_nothing_is_stored()
    {
        var user = await Merchants.CreateUserAsync(Factory, "no-key@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await client.PostAsync(
            PaymentRequests.Path, System.Net.Http.Json.JsonContent.Create(CreatePaymentTests.Body("4242424242424242")), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_required", await Problems.ReadCodeAsync(response));
        Assert.Equal(0, await CountPaymentsAsync(user.MerchantId));
    }

    [Theory]
    [InlineData("   ")]
    [InlineData("clé-avec-accent")]
    [InlineData("tab\there")]
    public async Task A_key_that_is_not_printable_ascii_is_refused(string key)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"bad-key-{Guid.NewGuid():N}@example.com");
        using var client = Clients.For(Factory, user);
        using var request = new HttpRequestMessage(HttpMethod.Post, PaymentRequests.Path)
        {
            Content = System.Net.Http.Json.JsonContent.Create(CreatePaymentTests.Body("4242424242424242")),
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", key);

        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_invalid", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_key_longer_than_255_characters_is_refused()
    {
        var user = await Merchants.CreateUserAsync(Factory, "long-key@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242"), new string('k', 256));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("idempotency_key_invalid", await Problems.ReadCodeAsync(response));
    }

    [Theory]
    [InlineData("4242424242424242", "authorized")]
    [InlineData("4000000000000002", "failed")]
    public async Task A_retry_gets_the_same_response_marked_as_replayed_and_charges_once(string number, string status)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"retry-{status}@example.com");
        using var client = Clients.For(Factory, user);
        var body = CreatePaymentTests.Body(number, amount: 4_200);

        using var first = await PaymentRequests.CreateAsync(client, body, "order-42-attempt");
        using var second = await PaymentRequests.CreateAsync(client, body, "order-42-attempt");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.False(first.Headers.Contains("Idempotent-Replayed"));
        Assert.Equal("true", second.Headers.GetValues("Idempotent-Replayed").Single());
        Assert.Equal(first.Headers.Location, second.Headers.Location);
        Assert.Equal(await first.Content.ReadAsStringAsync(Cancellation), await second.Content.ReadAsStringAsync(Cancellation));
        using var document = JsonDocument.Parse(await second.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(status, document.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, await CountPaymentsAsync(user.MerchantId));
    }

    [Theory]
    [InlineData(4_201L, "4242424242424242")]
    [InlineData(4_200L, "5555555555554444")]
    public async Task The_same_key_for_a_different_request_is_a_422(long amount, string number)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"reused-{amount}-{number[^4..]}@example.com");
        using var client = Clients.For(Factory, user);
        using var first = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242", amount: 4_200), "reused-key");

        using var second = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body(number, amount: amount), "reused-key");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        Assert.Equal("idempotency_key_reused", await Problems.ReadCodeAsync(second));
        Assert.Equal(1, await CountPaymentsAsync(user.MerchantId));
    }

    [Fact]
    public async Task Keys_belong_to_one_merchant_and_compare_exactly()
    {
        var first = await Merchants.CreateUserAsync(Factory, "keys-one@example.com");
        var second = await Merchants.CreateUserAsync(Factory, "keys-two@example.com", merchantName: "Second");
        using var firstClient = Clients.For(Factory, first);
        using var secondClient = Clients.For(Factory, second);
        var body = CreatePaymentTests.Body("4242424242424242");

        using var a = await PaymentRequests.CreateAsync(firstClient, body, "Shared-Key");
        using var b = await PaymentRequests.CreateAsync(secondClient, body, "Shared-Key");
        using var c = await PaymentRequests.CreateAsync(firstClient, body, "shared-key");

        Assert.All(new[] { a, b, c }, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        Assert.All(new[] { a, b, c }, response => Assert.False(response.Headers.Contains("Idempotent-Replayed")));
        Assert.Equal(2, await CountPaymentsAsync(first.MerchantId));
        Assert.Equal(1, await CountPaymentsAsync(second.MerchantId));
    }

    [Fact]
    public async Task Concurrent_retries_with_one_key_create_one_payment()
    {
        // A slow card network, so every retry arrives while the first request is still waiting on it.
        await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting("Acquirer:Simulated:LatencyMilliseconds", "1500"));
        var user = await Merchants.CreateUserAsync(factory, "concurrent@example.com");
        using var client = Clients.For(factory, user);
        var body = CreatePaymentTests.Body("4242424242424242", amount: 7_700);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => PaymentRequests.CreateAsync(client, body, "double-click")));
        using var afterwards = await PaymentRequests.CreateAsync(client, body, "double-click");

        try
        {
            var statuses = responses.Select(response => response.StatusCode).ToList();
            Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
            Assert.Equal(5, statuses.Count(status => status == HttpStatusCode.Conflict));
            foreach (var conflict in responses.Where(response => response.StatusCode == HttpStatusCode.Conflict))
            {
                Assert.Equal("idempotency_request_in_progress", await Problems.ReadCodeAsync(conflict));
            }

            Assert.Equal(HttpStatusCode.Created, afterwards.StatusCode);
            Assert.Equal("true", afterwards.Headers.GetValues("Idempotent-Replayed").Single());
            Assert.Equal(1, await CountPaymentsAsync(user.MerchantId, factory));
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
    public async Task An_invalid_request_stores_nothing_so_the_corrected_one_may_use_the_key()
    {
        var user = await Merchants.CreateUserAsync(Factory, "corrected@example.com");
        using var client = Clients.For(Factory, user);

        using var invalid = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424241"), "fix-and-retry");
        using var refused = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4111111111111111"), "fix-and-retry");
        using var corrected = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242"), "fix-and-retry");

        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, refused.StatusCode);
        Assert.Equal("card_not_accepted", await Problems.ReadCodeAsync(refused));
        Assert.Equal(HttpStatusCode.Created, corrected.StatusCode);
        Assert.False(corrected.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task A_key_held_by_a_live_request_is_a_409()
    {
        var user = await Merchants.CreateUserAsync(Factory, "held@example.com");
        var body = CreatePaymentTests.Body("4242424242424242");
        await SeedKeyAsync(user, "held-key", body, claimedAgo: TimeSpan.FromSeconds(1));
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, body, "held-key");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("idempotency_request_in_progress", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_retry_after_a_crash_finishes_the_payment_the_dead_request_created()
    {
        // The first request saved its payment as Pending, attached it to the key, and died before the acquirer
        // answered. The retry takes over the abandoned key and authorizes that payment, not a new one.
        var user = await Merchants.CreateUserAsync(Factory, "crashed@example.com");
        var body = CreatePaymentTests.Body("4242424242424242", amount: 3_300);
        var orphan = await SeedPendingPaymentAsync(user, 3_300);
        await SeedKeyAsync(user, "crash-key", body, claimedAgo: IdempotencyStore.ClaimTimeout + TimeSpan.FromSeconds(5), payment: orphan);
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, body, "crash-key");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(orphan.Value, document.RootElement.GetProperty("id").GetGuid());
        Assert.Equal("authorized", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("history").GetArrayLength());
        Assert.Equal(1, await CountPaymentsAsync(user.MerchantId));
    }

    [Fact]
    public async Task A_retry_after_a_crash_with_nothing_created_starts_over()
    {
        var user = await Merchants.CreateUserAsync(Factory, "crashed-early@example.com");
        var body = CreatePaymentTests.Body("4242424242424242");
        await SeedKeyAsync(user, "early-crash", body, claimedAgo: IdempotencyStore.ClaimTimeout + TimeSpan.FromSeconds(5));
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, body, "early-crash");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountPaymentsAsync(user.MerchantId));
    }

    [Fact]
    public async Task An_expired_key_is_a_new_key()
    {
        var user = await Merchants.CreateUserAsync(Factory, "expired-key@example.com");
        await SeedKeyAsync(user, "old-key", CreatePaymentTests.Body("4242424242424242", amount: 100), claimedAgo: TimeSpan.FromHours(25), completed: true);
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242", amount: 999), "old-key");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(response.Headers.Contains("Idempotent-Replayed"));
    }

    [Fact]
    public async Task The_stored_response_holds_no_card_number_or_cvv()
    {
        var user = await Merchants.CreateUserAsync(Factory, "stored-response@example.com");
        using var client = Clients.For(Factory, user);
        using var response = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242 4242 4242 4242", cvv: "864"), "stored-key");

        await using var scope = Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().IdempotencyKeys
            .AsNoTracking().SingleAsync(key => key.MerchantId == user.MerchantId, Cancellation);
        Assert.Equal(IdempotencyStatus.Completed, stored.Status);
        Assert.Equal(201, stored.ResponseStatus);
        Assert.DoesNotContain("4242424242424242", stored.ResponseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("\"864\"", stored.ResponseBody, StringComparison.Ordinal);
        Assert.DoesNotContain("tok_sim_", stored.ResponseBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_cleanup_deletes_expired_keys_and_leaves_live_ones()
    {
        var user = await Merchants.CreateUserAsync(Factory, "cleanup@example.com");
        var body = CreatePaymentTests.Body("4242424242424242");
        await SeedKeyAsync(user, "expired-done", body, claimedAgo: TimeSpan.FromHours(25), completed: true);
        await SeedKeyAsync(user, "expired-abandoned", body, claimedAgo: TimeSpan.FromHours(25));
        await SeedKeyAsync(user, "fresh-done", body, claimedAgo: TimeSpan.FromMinutes(5), completed: true);
        await SeedKeyAsync(user, "fresh-running", body, claimedAgo: TimeSpan.FromSeconds(1));

        await using var scope = Factory.Services.CreateAsyncScope();
        var deleted = await scope.ServiceProvider.GetRequiredService<DeleteExpiredIdempotencyKeys>().HandleAsync(Cancellation);

        Assert.Equal(2, deleted);
        var left = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().IdempotencyKeys
            .Where(key => key.MerchantId == user.MerchantId).Select(key => key.Key).OrderBy(key => key).ToListAsync(Cancellation);
        Assert.Equal(["fresh-done", "fresh-running"], left);
    }

    private async Task SeedKeyAsync(
        TestUser user, string key, object body, TimeSpan claimedAgo, bool completed = false, PaymentId? payment = null)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var json = JsonSerializer.Serialize(body);
        var request = JsonSerializer.Deserialize<CreatePaymentRequest>(json, SnakeCase)!;
        var row = IdempotencyKey.Claim(
            user.MerchantId, key, request.Fingerprint(), TimeProvider.System.GetUtcNow() - claimedAgo, IdempotencyStore.Retention);
        if (payment is { } id)
        {
            row.AttachPayment(id);
        }

        if (completed)
        {
            row.Complete(201, """{"seeded":true}""", null);
        }

        db.IdempotencyKeys.Add(row);
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task<PaymentId> SeedPendingPaymentAsync(TestUser user, long amount)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var vault = scope.ServiceProvider.GetRequiredService<ICardVault>();
        var now = TimeProvider.System.GetUtcNow();
        var card = vault.Tokenize(new CardInput("4242424242424242", 12, ExpiryYear, "123")).Value;
        var payment = Payment.Create(
            PaymentId.New(now), user.MerchantId, Money.Create(amount, "USD").Value, card, null, Actor.MerchantUser(user.UserId.Value), now);
        db.Payments.Add(payment);
        db.RecordChanges(payment, "crashed-request");
        await db.SaveChangesAsync(Cancellation);
        return payment.Id;
    }

    private async Task<int> CountPaymentsAsync(MerchantId merchantId, Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>? factory = null)
    {
        await using var scope = (factory ?? Factory).Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments
            .CountAsync(payment => payment.MerchantId == merchantId, Cancellation);
    }
}
