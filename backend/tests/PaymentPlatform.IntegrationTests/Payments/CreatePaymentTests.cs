using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Payments;

public sealed class CreatePaymentTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string PaymentsPath = "/api/v1/payments";
    private static readonly int ExpiryYear = TimeProvider.System.GetUtcNow().Year + 3;

    [Fact]
    public async Task An_approved_card_creates_an_authorized_payment_with_its_whole_history()
    {
        var user = await Merchants.CreateUserAsync(Factory, "approved@example.com");
        using var client = Clients.For(Factory, user);
        client.DefaultRequestHeaders.Add("X-Correlation-Id", "create-approved");

        using var response = await PaymentRequests.CreateAsync(client, Body("4242 4242 4242 4242", amount: 1_250, reference: "order-1001"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadAsync(response);
        var payment = body.RootElement;
        Assert.Equal($"{PaymentsPath}/{payment.GetProperty("id").GetGuid()}", response.Headers.Location?.ToString());
        Assert.Equal("authorized", payment.GetProperty("status").GetString());
        Assert.Equal(1_250, payment.GetProperty("amount_minor").GetInt64());
        Assert.Equal("USD", payment.GetProperty("currency").GetString());
        Assert.Equal("order-1001", payment.GetProperty("merchant_reference").GetString());
        Assert.StartsWith("sim_auth_", payment.GetProperty("authorization_reference").GetString(), StringComparison.Ordinal);
        Assert.Equal(JsonValueKind.Null, payment.GetProperty("failure_reason").ValueKind);
        var card = payment.GetProperty("card");
        Assert.Equal("visa", card.GetProperty("brand").GetString());
        Assert.Equal("4242", card.GetProperty("last4").GetString());
        Assert.Equal(12, card.GetProperty("exp_month").GetInt32());
        Assert.Equal(ExpiryYear, card.GetProperty("exp_year").GetInt32());
        Assert.False(card.TryGetProperty("token", out _), "the vault token stays on the server");

        var history = payment.GetProperty("history").EnumerateArray().ToList();
        Assert.Equal(2, history.Count);
        Assert.Equal(JsonValueKind.Null, history[0].GetProperty("from").ValueKind);
        Assert.Equal("pending", history[0].GetProperty("to").GetString());
        Assert.Equal("pending", history[1].GetProperty("from").GetString());
        Assert.Equal("authorized", history[1].GetProperty("to").GetString());
        foreach (var entry in history)
        {
            Assert.Equal("merchant_user", entry.GetProperty("actor").GetProperty("kind").GetString());
            Assert.Equal(user.UserId.ToString(), entry.GetProperty("actor").GetProperty("id").GetString());
            Assert.Equal("create-approved", entry.GetProperty("correlation_id").GetString());
        }
    }

    [Theory]
    [InlineData("4000000000000002", "card_declined")]
    [InlineData("4000000000009995", "insufficient_funds")]
    public async Task A_declined_card_creates_a_failed_payment_that_keeps_the_reason(string number, string reason)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"declined-{reason}@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, Body(number));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadAsync(response);
        Assert.Equal("failed", body.RootElement.GetProperty("status").GetString());
        Assert.Equal(reason, body.RootElement.GetProperty("failure_reason").GetString());
        var last = body.RootElement.GetProperty("history").EnumerateArray().Last();
        Assert.Equal("failed", last.GetProperty("to").GetString());
        Assert.Equal(reason, last.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task When_the_acquirer_does_not_answer_the_payment_stays_pending_and_visible()
    {
        var user = await Merchants.CreateUserAsync(Factory, "silent-acquirer@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, Body("4000000000000119"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadAsync(response);
        Assert.Equal("pending", body.RootElement.GetProperty("status").GetString());
        Assert.Single(body.RootElement.GetProperty("history").EnumerateArray());
        using var stored = await client.GetAsync(response.Headers.Location, Cancellation);
        using var read = await ReadAsync(stored);
        Assert.Equal("pending", read.RootElement.GetProperty("status").GetString());
    }

    [Theory]
    [InlineData("5555555555554444", "123", "mastercard", "4444")]
    [InlineData("378282246310005", "1234", "amex", "0005")]
    [InlineData("6011-1111-1111-1117", "123", "discover", "1117")]
    public async Task Every_brand_test_card_is_accepted(string number, string cvv, string brand, string last4)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"brand-{brand}@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, Body(number, cvv: cvv, currency: "eur"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = await ReadAsync(response);
        Assert.Equal("authorized", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("EUR", body.RootElement.GetProperty("currency").GetString());
        Assert.Equal(brand, body.RootElement.GetProperty("card").GetProperty("brand").GetString());
        Assert.Equal(last4, body.RootElement.GetProperty("card").GetProperty("last4").GetString());
    }

    public static TheoryData<string, string, string> InvalidBodies()
    {
        var year = ExpiryYear;
        var now = TimeProvider.System.GetUtcNow();
        string Card(string number = "4242424242424242", string month = "12", string? yearText = null, string cvv = "\"123\"") =>
            $$"""{"number":"{{number}}","exp_month":{{month}},"exp_year":{{yearText ?? year.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"cvv":{{cvv}}}""";
        string Payment(string amount = "1250", string currency = "\"USD\"", string? card = null, string extra = "") =>
            $$"""{"amount_minor":{{amount}},"currency":{{currency}},"card":{{card ?? Card()}}{{extra}}}""";
        var lastMonth = now.AddMonths(-1);
        return new TheoryData<string, string, string>
        {
            { """{"currency":"USD","card":null}""", "amount_minor", "amount_required" },
            { Payment(amount: "0"), "amount_minor", "amount_must_be_positive" },
            { Payment(amount: "-5"), "amount_minor", "amount_must_be_positive" },
            { Payment(amount: "100000000"), "amount_minor", "amount_too_large" },
            { Payment(currency: "null"), "currency", "currency_required" },
            { Payment(currency: "\"XYZ\""), "currency", "currency_not_supported" },
            { Payment(extra: ""","merchant_reference":"   " """), "merchant_reference", "merchant_reference_invalid" },
            { Payment(extra: $$""","merchant_reference":"{{new string('r', 101)}}" """), "merchant_reference", "merchant_reference_invalid" },
            { """{"amount_minor":1250,"currency":"USD"}""", "card", "card_required" },
            { Payment(card: Card(number: "")), "card.number", "card_number_required" },
            { Payment(card: Card(number: "4242424242424241")), "card.number", "card_number_invalid" },
            { Payment(card: Card(number: "4242abcd42424242")), "card.number", "card_number_invalid" },
            { Payment(card: Card(number: "9999999999999995")), "card.number", "card_brand_not_supported" },
            { Payment(card: Card(month: "13")), "card.exp_month", "card_exp_month_invalid" },
            { Payment(card: Card(month: "null")), "card.exp_month", "card_exp_month_required" },
            { Payment(card: Card(yearText: (now.Year - 1).ToString(System.Globalization.CultureInfo.InvariantCulture))), "card.exp_year", "card_expired" },
            { Payment(card: Card(yearText: (now.Year + 30).ToString(System.Globalization.CultureInfo.InvariantCulture))), "card.exp_year", "card_exp_year_invalid" },
            {
                Payment(card: Card(month: lastMonth.Month.ToString(System.Globalization.CultureInfo.InvariantCulture), yearText: lastMonth.Year.ToString(System.Globalization.CultureInfo.InvariantCulture))),
                lastMonth.Year == now.Year ? "card.exp_month" : "card.exp_year",
                "card_expired"
            },
            { Payment(card: Card(cvv: "null")), "card.cvv", "card_cvv_required" },
            { Payment(card: Card(cvv: "\"12\"")), "card.cvv", "card_cvv_invalid" },
            { Payment(card: Card(cvv: "\"1234\"")), "card.cvv", "card_cvv_invalid" },
            { Payment(card: Card(number: "378282246310005", cvv: "\"123\"")), "card.cvv", "card_cvv_invalid" },
        };
    }

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task An_invalid_request_is_a_400_with_a_code_for_the_field(string json, string field, string code)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"invalid-{Guid.NewGuid():N}@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateRawAsync(client, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await Problems.ReadAsync(response);
        Assert.Equal("invalid_request", problem.RootElement.GetProperty("code").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty(field, out var codes), $"no error for {field} in {errors}");
        Assert.Contains(code, codes.EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(0, await CountPaymentsAsync(user.MerchantId));
    }

    [Fact]
    public async Task A_valid_card_that_is_not_a_test_card_is_refused_and_nothing_is_stored()
    {
        var user = await Merchants.CreateUserAsync(Factory, "real-card@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await PaymentRequests.CreateAsync(client, Body("4111111111111111"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("card_not_accepted", await Problems.ReadCodeAsync(response));
        Assert.Equal(0, await CountPaymentsAsync(user.MerchantId));
    }

    [Fact]
    public async Task Without_a_token_no_payment_is_created()
    {
        using var client = Factory.CreateClient();

        using var response = await PaymentRequests.CreateAsync(client, Body("4242424242424242"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task The_payment_belongs_to_the_merchant_in_the_token_whatever_the_body_says()
    {
        var user = await Merchants.CreateUserAsync(Factory, "scoped-create@example.com");
        var other = await Merchants.CreateUserAsync(Factory, "other-create@example.com");
        using var client = Clients.For(Factory, user);
        var json = $$$"""{"amount_minor":500,"currency":"USD","merchant_id":"{{{other.MerchantId}}}","card":{"number":"4242424242424242","exp_month":12,"exp_year":{{{ExpiryYear}}},"cvv":"123"}}""";

        using var response = await PaymentRequests.CreateRawAsync(client, json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountPaymentsAsync(user.MerchantId));
        Assert.Equal(0, await CountPaymentsAsync(other.MerchantId));
    }

    [Fact]
    public async Task No_card_number_or_cvv_is_stored_logged_or_returned()
    {
        const string number = "4242424242424242";
        const string cvv = "739";
        await using var factory = LogCapture.Attach(Factory);
        var user = await Merchants.CreateUserAsync(factory, "pci@example.com");
        using var client = Clients.For(factory, user);

        using var response = await PaymentRequests.CreateAsync(client, Body("4242 4242 4242 4242", cvv: cvv));
        var returned = await response.Content.ReadAsStringAsync(Cancellation);
        using var invalid = await PaymentRequests.CreateAsync(client, Body("4242 4242 4242 4241", cvv: cvv));
        var problem = await invalid.Content.ReadAsStringAsync(Cancellation);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        foreach (var text in new[] { returned, problem })
        {
            Assert.DoesNotContain(number, text, StringComparison.Ordinal);
            Assert.DoesNotContain("4242 4242", text, StringComparison.Ordinal);
            Assert.DoesNotContain("cvv", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("tok_sim_", text, StringComparison.Ordinal);
            AssertNoJsonValue(cvv, text);
        }

        var logs = LogCapture.Lines(factory);
        Assert.NotEmpty(logs);
        Assert.DoesNotContain(logs, line => line.Contains("4242", StringComparison.Ordinal));
        Assert.DoesNotContain(logs, line => line.Contains("cvv", StringComparison.OrdinalIgnoreCase));

        // Every column of every row the create wrote, as SQL Server sees them.
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var payments = await db.Database.SqlQuery<string>($"SELECT (SELECT * FROM payments.Payments FOR JSON PATH) AS [Value]").ToListAsync(Cancellation);
        var events = await db.Database.SqlQuery<string>($"SELECT (SELECT * FROM payments.PaymentEvents FOR JSON PATH) AS [Value]").ToListAsync(Cancellation);
        var rows = string.Join('\n', payments.Concat(events));
        Assert.Contains("4242", rows, StringComparison.Ordinal);
        Assert.DoesNotContain(number, rows, StringComparison.Ordinal);
        AssertNoJsonValue(cvv, rows);
    }

    [Fact]
    public async Task Saving_a_status_change_without_its_audit_rows_is_refused()
    {
        var user = await Merchants.CreateUserAsync(Factory, "unaudited@example.com");
        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var now = TimeProvider.System.GetUtcNow();
        var payment = Payment.Create(
            PaymentId.New(now), user.MerchantId, Money.Create(100, "USD").Value,
            new PaymentCard("tok_sim_x", CardBrand.Visa, "4242", 12, ExpiryYear), null, Actor.MerchantUser(user.UserId.Value), now);
        db.Payments.Add(payment);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Cancellation));

        Assert.Contains("audit", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, await CountPaymentsAsync(user.MerchantId));
    }

    internal static object Body(string number, long amount = 1_000, string currency = "USD", string cvv = "123", string? reference = null) =>
        new
        {
            amount_minor = amount,
            currency,
            merchant_reference = reference,
            card = new { number, exp_month = 12, exp_year = ExpiryYear, cvv },
        };

    /// <summary>
    /// The value as a whole JSON string or number. A plain substring search would also match it inside a random
    /// id (a correlation id once held "739"), which made an earlier version of this check flaky.
    /// </summary>
    private static void AssertNoJsonValue(string value, string json)
    {
        foreach (var form in new[] { $"\"{value}\"", $":{value},", $":{value}}}", $"[{value},", $",{value}]" })
        {
            Assert.DoesNotContain(form, json, StringComparison.Ordinal);
        }
    }

    private static async Task<JsonDocument> ReadAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

    private async Task<int> CountPaymentsAsync(MerchantId merchantId)
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Payments
            .CountAsync(payment => payment.MerchantId == merchantId, Cancellation);
    }
}
