using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Demo;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Payments;

/// <summary><c>GET /api/v1/payments</c>: keyset pages, filters, and merchant scoping, checked against the database's own order.</summary>
public sealed class ListPaymentsTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task Paging_visits_every_payment_exactly_once_newest_first()
    {
        await using var factory = SeededFactory();
        using var client = await DemoClientAsync(factory);
        var expected = await IdsInDatabaseOrderAsync(factory, _ => true);

        var seen = await PageThroughAsync(client, "limit=37");

        Assert.Equal(DemoPayments.All.Count, expected.Count);
        Assert.Equal(expected, seen);
    }

    [Fact]
    public async Task The_default_page_is_25_and_the_last_page_has_no_cursor()
    {
        await using var factory = SeededFactory();
        using var client = await DemoClientAsync(factory);

        using var first = await GetAsync(client, string.Empty);
        using var tail = await GetAsync(client, $"limit=100&created_from={Uri.EscapeDataString(TimeProvider.System.GetUtcNow().AddDays(-1).ToString("O"))}");

        Assert.Equal(25, first.RootElement.GetProperty("data").GetArrayLength());
        Assert.Equal(JsonValueKind.String, first.RootElement.GetProperty("next_cursor").ValueKind);
        Assert.Equal(JsonValueKind.Null, tail.RootElement.GetProperty("next_cursor").ValueKind);
        var item = first.RootElement.GetProperty("data")[0];
        Assert.False(item.TryGetProperty("history", out _), "list items are summaries");
        Assert.True(item.GetProperty("card").TryGetProperty("last4", out _));
    }

    [Fact]
    public async Task Payments_created_at_the_same_instant_keep_their_insertion_order_and_none_is_lost_between_pages()
    {
        var user = await Merchants.CreateUserAsync(Factory, "ties@example.com");
        var instant = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var vault = scope.ServiceProvider.GetRequiredService<ICardVault>();
            for (var index = 0; index < 7; index++)
            {
                var card = vault.Tokenize(new CardInput("4242424242424242", 12, 2030, "123")).Value;
                var payment = Payment.Create(
                    new PaymentId(Guid.NewGuid()), user.MerchantId, Money.Create(100 + index, "USD").Value, card, null, Actor.MerchantUser(user.UserId.Value), instant);
                db.Payments.Add(payment);
                db.RecordChanges(payment, "ties");
            }

            await db.SaveChangesAsync(Cancellation);
        }

        using var client = Clients.For(Factory, user);
        var seen = await PageThroughAsync(client, "limit=2");

        Assert.Equal(await IdsInDatabaseOrderAsync(Factory, payment => payment.MerchantId == user.MerchantId), seen);
        Assert.Equal(7, seen.Distinct().Count());
    }

    public static TheoryData<string, string> Filters() => new()
    {
        { "status=settled", "settled" },
        { "status=captured,authorized", "captured or authorized" },
        { "status=failed&status=pending", "failed or pending" },
        { "currency=eur", "EUR" },
        { "amount_min_minor=10000&amount_max_minor=50000", "between 100.00 and 500.00" },
        { "reference=INV-11", "reference starting INV-11" },
        { "status=settled&currency=USD&amount_min_minor=2000", "settled USD from 20.00" },
        { "created_from=CREATED_FROM&created_to=CREATED_TO", "created 60 to 30 days ago" },
    };

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task Each_filter_returns_exactly_the_matching_payments(string filter, string description)
    {
        await using var factory = SeededFactory();
        using var client = await DemoClientAsync(factory);
        var now = TimeProvider.System.GetUtcNow();
        var from = now.AddDays(-60);
        var to = now.AddDays(-30);
        var query = filter
            .Replace("CREATED_FROM", Uri.EscapeDataString(from.ToString("O")), StringComparison.Ordinal)
            .Replace("CREATED_TO", Uri.EscapeDataString(to.ToString("O")), StringComparison.Ordinal);
        var all = await LoadAllAsync(factory);
        var matching = filter switch
        {
            "status=settled" => all.Where(payment => payment.Status == PaymentStatus.Settled),
            "status=captured,authorized" => all.Where(payment => payment.Status is PaymentStatus.Captured or PaymentStatus.Authorized),
            "status=failed&status=pending" => all.Where(payment => payment.Status is PaymentStatus.Failed or PaymentStatus.Pending),
            "currency=eur" => all.Where(payment => payment.Amount.Currency.Code == "EUR"),
            "amount_min_minor=10000&amount_max_minor=50000" => all.Where(payment => payment.Amount.AmountMinor is >= 10_000 and <= 50_000),
            "reference=INV-11" => all.Where(payment => payment.MerchantReference!.StartsWith("INV-11", StringComparison.Ordinal)),
            "status=settled&currency=USD&amount_min_minor=2000" => all.Where(payment =>
                payment.Status == PaymentStatus.Settled && payment.Amount.Currency.Code == "USD" && payment.Amount.AmountMinor >= 2_000),
            _ => all.Where(payment => payment.CreatedAt >= from && payment.CreatedAt < to),
        };
        var expected = matching.Select(payment => payment.Id.Value).ToHashSet();

        var seen = await PageThroughAsync(client, $"limit=50&{query}");

        Assert.NotEmpty(expected);
        Assert.True(expected.SetEquals(seen), $"{description}: expected {expected.Count}, got {seen.Count}");
        Assert.Equal(seen.Count, seen.Distinct().Count());
    }

    [Fact]
    public async Task Another_merchants_payments_never_appear()
    {
        await using var factory = SeededFactory();
        var stranger = await Merchants.CreateUserAsync(factory, "list-stranger@example.com", merchantName: "Elsewhere");
        using var strangerClient = Clients.For(factory, stranger);
        using var mine = await PaymentRequests.CreateAsync(strangerClient, CreatePaymentTests.Body("4242424242424242"));
        using var demoClient = await DemoClientAsync(factory);

        var strangerSees = await PageThroughAsync(strangerClient, "limit=100");
        var demoSees = await PageThroughAsync(demoClient, "limit=100");

        Assert.Single(strangerSees);
        Assert.DoesNotContain(strangerSees[0], demoSees);
        Assert.Equal(DemoPayments.All.Count, demoSees.Count);
    }

    [Theory]
    [InlineData("limit=0", "limit", "invalid_limit")]
    [InlineData("limit=101", "limit", "invalid_limit")]
    [InlineData("limit=ten", "limit", "invalid_limit")]
    [InlineData("cursor=not-a-cursor!", "cursor", "invalid_cursor")]
    [InlineData("cursor=AAAA", "cursor", "invalid_cursor")]
    [InlineData("status=paid", "status", "status_invalid")]
    [InlineData("status=1", "status", "status_invalid")]
    [InlineData("created_from=yesterday", "created_from", "created_from_invalid")]
    [InlineData("created_from=2026-10-02T00:00:00Z&created_to=2026-10-01T00:00:00Z", "created_to", "created_range_invalid")]
    [InlineData("amount_min_minor=-1", "amount_min_minor", "amount_min_minor_invalid")]
    [InlineData("amount_min_minor=500&amount_max_minor=100", "amount_max_minor", "amount_range_invalid")]
    [InlineData("currency=XYZ", "currency", "currency_not_supported")]
    [InlineData("reference=%20%20", "reference", "reference_invalid")]
    public async Task A_bad_parameter_is_a_400_with_a_code_for_it(string query, string field, string code)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"list-bad-{Guid.NewGuid():N}@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await client.GetAsync($"{PaymentRequests.Path}?{query}", Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await Problems.ReadAsync(response);
        Assert.Equal("invalid_request", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(code, problem.RootElement.GetProperty("errors").GetProperty(field).EnumerateArray().Select(value => value.GetString()));
    }

    [Fact]
    public async Task Without_a_token_nothing_is_listed()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(PaymentRequests.Path, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private WebApplicationFactory<Program> SeededFactory() =>
        Factory.WithWebHostBuilder(builder => builder.UseSetting(DemoSeed.SettingName, "true"));

    private static async Task<HttpClient> DemoClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = DemoSeed.Email, password = DemoSeed.Password }, TestContext.Current.CancellationToken);
        using var token = JsonDocument.Parse(await login.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.RootElement.GetProperty("access_token").GetString());
        return client;
    }

    private static async Task<JsonDocument> GetAsync(HttpClient client, string query)
    {
        using var response = await client.GetAsync($"{PaymentRequests.Path}?{query}", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>Follows <c>next_cursor</c> to the end and returns every id in the order the pages gave them.</summary>
    private static async Task<List<Guid>> PageThroughAsync(HttpClient client, string query)
    {
        var ids = new List<Guid>();
        string? cursor = null;
        for (var page = 0; page < 1_000; page++)
        {
            using var body = await GetAsync(client, cursor is null ? query : $"{query}&cursor={cursor}");
            ids.AddRange(body.RootElement.GetProperty("data").EnumerateArray().Select(item => item.GetProperty("id").GetGuid()));
            var next = body.RootElement.GetProperty("next_cursor");
            if (next.ValueKind == JsonValueKind.Null)
            {
                return ids;
            }

            cursor = next.GetString();
        }

        throw new InvalidOperationException("the pages never ended");
    }

    private static async Task<List<Guid>> IdsInDatabaseOrderAsync(WebApplicationFactory<Program> factory, System.Linq.Expressions.Expression<Func<Payment, bool>> filter)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var ids = await db.Payments.Where(filter)
            .OrderByDescending(payment => payment.CreatedAt).ThenByDescending(payment => EF.Property<long>(payment, PaymentsDbContext.PaymentSequence))
            .Select(payment => payment.Id).ToListAsync(TestContext.Current.CancellationToken);
        return ids.Select(id => id.Value).ToList();
    }

    private static async Task<List<Payment>> LoadAllAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments.AsNoTracking()
            .Where(payment => payment.MerchantId == DemoSeed.MerchantId).ToListAsync(TestContext.Current.CancellationToken);
    }
}
