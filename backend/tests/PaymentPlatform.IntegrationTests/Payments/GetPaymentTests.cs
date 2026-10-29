using System.Net;
using System.Text.Json;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Payments;

public sealed class GetPaymentTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task A_merchant_reads_back_exactly_what_was_created()
    {
        var user = await Merchants.CreateUserAsync(Factory, "reader-pay@example.com");
        using var client = Clients.For(Factory, user);
        using var created = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242", reference: "inv-7"));
        var createdBody = await created.Content.ReadAsStringAsync(Cancellation);

        using var read = await client.GetAsync(created.Headers.Location, Cancellation);

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using var expected = JsonDocument.Parse(createdBody);
        using var actual = JsonDocument.Parse(await read.Content.ReadAsStringAsync(Cancellation));
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement), $"created {createdBody}, read {actual.RootElement}");
    }

    [Fact]
    public async Task Another_merchants_payment_is_a_404_like_one_that_does_not_exist()
    {
        var owner = await Merchants.CreateUserAsync(Factory, "owner-pay@example.com");
        var stranger = await Merchants.CreateUserAsync(Factory, "stranger-pay@example.com", merchantName: "Elsewhere");
        using var ownerClient = Clients.For(Factory, owner);
        using var created = await PaymentRequests.CreateAsync(ownerClient, CreatePaymentTests.Body("4242424242424242"));
        using var strangerClient = Clients.For(Factory, stranger);

        using var someoneElses = await strangerClient.GetAsync(created.Headers.Location, Cancellation);
        using var missing = await strangerClient.GetAsync($"/api/v1/payments/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, someoneElses.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var first = await Problems.ReadAsync(someoneElses);
        using var second = await Problems.ReadAsync(missing);
        Assert.Equal("not_found", first.RootElement.GetProperty("code").GetString());
        Assert.Equal(first.RootElement.GetProperty("detail").GetString(), second.RootElement.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("123")]
    public async Task A_path_that_is_not_a_payment_id_is_a_404(string id)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"bad-id-{id}@example.com");
        using var client = Clients.For(Factory, user);

        using var response = await client.GetAsync($"/api/v1/payments/{id}", Cancellation);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("not_found", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task Without_a_token_a_payment_cannot_be_read()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync($"/api/v1/payments/{Guid.NewGuid()}", Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
