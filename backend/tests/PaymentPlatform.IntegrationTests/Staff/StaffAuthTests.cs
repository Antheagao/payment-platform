using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Domain.Staff;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Staff;

/// <summary>
/// Platform staff sign in on their own route and get a token of their own kind (ADR 011): it opens every staff route
/// and no merchant route, and a merchant's token opens no staff route.
/// </summary>
public sealed class StaffAuthTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string StaffLoginPath = "/api/v1/staff/auth/login";
    private const string Password = "a staff passphrase";

    [Fact]
    public async Task A_staff_login_returns_a_token_with_the_staff_role_the_staff_user_and_no_merchant()
    {
        var staffId = await CreateStaffAsync(Factory, "ops@example.com");
        using var client = Factory.CreateClient();

        using var response = await LogInAsync(client, "ops@example.com", Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(1800, body.RootElement.GetProperty("expires_in").GetInt32());
        var token = new JsonWebToken(body.RootElement.GetProperty("access_token").GetString());
        Assert.Equal("HS256", token.Alg);
        Assert.Equal(staffId.ToString(), token.Subject);
        Assert.Equal("staff", token.GetClaim("role").Value);
        Assert.False(token.TryGetClaim("merchant_id", out _));
    }

    [Fact]
    public async Task The_staff_token_names_the_staff_user()
    {
        var staffId = await CreateStaffAsync(Factory, "me@example.com");
        using var client = await StaffClientAsync(Factory, "me@example.com");

        using var response = await client.GetAsync("/api/v1/staff/me", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(staffId.Value, body.RootElement.GetProperty("staff_id").GetGuid());
        Assert.Equal("me@example.com", body.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_answer_and_a_bad_request_its_field_codes()
    {
        await CreateStaffAsync(Factory, "known@example.com");
        using var client = Factory.CreateClient();

        using var wrong = await LogInAsync(client, "known@example.com", "not it");
        using var unknown = await LogInAsync(client, "nobody@example.com", Password);
        using var invalid = await client.PostAsJsonAsync(StaffLoginPath, new { email = "not an email" }, Cancellation);

        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_credentials"), (wrong.StatusCode, await Problems.ReadCodeAsync(wrong)));
        Assert.Equal((HttpStatusCode.Unauthorized, "invalid_credentials"), (unknown.StatusCode, await Problems.ReadCodeAsync(unknown)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var problem = await Problems.ReadAsync(invalid);
        Assert.Equal("email_invalid", problem.RootElement.GetProperty("errors").GetProperty("email")[0].GetString());
        Assert.Equal("password_required", problem.RootElement.GetProperty("errors").GetProperty("password")[0].GetString());
    }

    [Fact]
    public async Task Merchant_users_and_staff_cannot_sign_in_on_each_others_route()
    {
        var merchant = await Merchants.CreateUserAsync(Factory, "merchant-person@example.com");
        await CreateStaffAsync(Factory, "staff-person@example.com");
        using var client = Factory.CreateClient();

        using var merchantAsStaff = await LogInAsync(client, merchant.Email, merchant.Password);
        using var staffAsMerchant = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = "staff-person@example.com", password = Password }, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, merchantAsStaff.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, staffAsMerchant.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/payments")]
    [InlineData("GET", "/api/v1/me")]
    [InlineData("GET", "/api/v1/webhook-endpoint")]
    public async Task A_staff_token_opens_no_merchant_route(string method, string path)
    {
        await CreateStaffAsync(Factory, "no-merchant@example.com");
        using var client = await StaffClientAsync(Factory, "no-merchant@example.com");

        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("GET", "/api/v1/staff/me")]
    [InlineData("GET", "/api/v1/staff/webhook-deliveries?status=dead_lettered")]
    [InlineData("POST", "/api/v1/staff/webhook-deliveries/1/replay")]
    public async Task A_merchant_token_opens_no_staff_route_and_no_token_opens_none(string method, string path)
    {
        var merchant = await Merchants.CreateUserAsync(Factory, "not-staff@example.com");
        using var merchantClient = Clients.For(Factory, merchant);
        using var anonymous = Factory.CreateClient();

        using var withMerchantToken = await merchantClient.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation);
        using var withoutToken = await anonymous.SendAsync(new HttpRequestMessage(new HttpMethod(method), path), Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, withMerchantToken.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, withoutToken.StatusCode);
    }

    [Fact]
    public async Task A_token_that_claims_both_kinds_opens_no_staff_route()
    {
        // Only the signing key could make one; even then, a merchant id disqualifies a token from the staff policy.
        var merchant = await Merchants.CreateUserAsync(Factory, "both-kinds@example.com");
        var claims = Tokens.ClaimsFor(merchant);
        claims["role"] = "staff";
        using var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.Forge(claims));

        using var response = await client.GetAsync("/api/v1/staff/me", Cancellation);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Staff_logins_have_their_own_per_email_limit()
    {
        await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting("Auth:LoginRateLimit:PerEmailPermits", "2"));
        await CreateStaffAsync(factory, "shared@example.com");
        using var client = factory.CreateClient();

        using var first = await LogInAsync(client, "shared@example.com", "guess one");
        using var second = await LogInAsync(client, "shared@example.com", "guess two");
        using var third = await LogInAsync(client, "shared@example.com", Password);
        using var merchantSide = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = "shared@example.com", password = "x" }, Cancellation);

        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(third.Headers.Contains("Retry-After"));
        // The merchant login's limit for the same email is untouched, and the other way round.
        Assert.Equal(HttpStatusCode.Unauthorized, merchantSide.StatusCode);
    }

    [Fact]
    public async Task No_password_or_token_reaches_the_logs()
    {
        await using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddFakeLogging();
        }));
        await CreateStaffAsync(factory, "private.operator@example.com");
        using var client = factory.CreateClient();

        using var good = await LogInAsync(client, "private.operator@example.com", Password);
        using var bad = await LogInAsync(client, "private.operator@example.com", "another-secret-guess");
        var token = JsonDocument.Parse(await good.Content.ReadAsStringAsync(Cancellation)).RootElement.GetProperty("access_token").GetString()!;

        var lines = factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => $"{record.Message} {string.Join(' ', record.StructuredState?.Select(pair => pair.Value) ?? [])}")
            .ToList();
        Assert.NotEmpty(lines);
        foreach (var secret in new[] { Password, "another-secret-guess", "private.operator", token })
        {
            Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    internal static async Task<StaffUserId> CreateStaffAsync(WebApplicationFactory<Program> factory, string email, string password = Password)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordVerifier>();
        var staff = StaffUser.Create(StaffUserId.New(TimeProvider.System.GetUtcNow()), email, passwords.Hash(password), TimeProvider.System.GetUtcNow());
        db.StaffUsers.Add(staff);
        await db.SaveChangesAsync(Cancellation);
        return staff.Id;
    }

    /// <summary>A client carrying a real staff token, from the staff login.</summary>
    internal static async Task<HttpClient> StaffClientAsync(WebApplicationFactory<Program> factory, string email, string password = Password)
    {
        var client = factory.CreateClient();
        using var login = await LogInAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync(Cancellation));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.RootElement.GetProperty("access_token").GetString());
        return client;
    }

    private static Task<HttpResponseMessage> LogInAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync(StaffLoginPath, new { email, password }, Cancellation);
}
