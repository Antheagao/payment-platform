using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Tokens;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Auth;

/// <summary>
/// How a request is validated (ADR 004): every way a bearer token can be missing, expired, forged, or bent,
/// against <c>GET /api/v1/me</c>, the protected endpoint that exists today.
/// </summary>
public sealed class AccessTokenTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string ProtectedPath = "/api/v1/me";

    [Fact]
    public async Task No_token_is_401_with_a_bare_bearer_challenge()
    {
        using var client = Factory.CreateClient();

        using var response = await client.GetAsync(ProtectedPath, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("unauthorized", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_valid_token_is_accepted()
    {
        var user = await Merchants.CreateUserAsync(Factory, "valid@example.com");

        using var response = await CallWithTokenAsync(Tokens.Forge(Tokens.ClaimsFor(user)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_token_the_api_issued_an_hour_ago_has_expired()
    {
        // The API issues the token on a clock an hour behind, so it expired 30 minutes ago by the real clock,
        // which is the one that validates it: issuance reads the injected clock, as it must.
        var clock = new FakeTimeProvider(TimeProvider.System.GetUtcNow().AddHours(-1));
        await using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<TimeProvider>(clock)));
        var user = await Merchants.CreateUserAsync(factory, "late@example.com");
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password }, Cancellation);
        using var body = JsonDocument.Parse(await login.Content.ReadAsStringAsync(Cancellation));
        var token = body.RootElement.GetProperty("access_token").GetString();

        using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("token_expired", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task Expiry_allows_thirty_seconds_of_clock_skew_and_no_more()
    {
        var user = await Merchants.CreateUserAsync(Factory, "skew@example.com");
        var now = TimeProvider.System.GetUtcNow();

        using var withinSkew = await CallWithTokenAsync(Tokens.Forge(Tokens.ClaimsFor(user), now.AddMinutes(-30).AddSeconds(-10)));
        using var beyondSkew = await CallWithTokenAsync(Tokens.Forge(Tokens.ClaimsFor(user), now.AddMinutes(-30).AddSeconds(-60)));

        Assert.Equal(HttpStatusCode.OK, withinSkew.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, beyondSkew.StatusCode);
        Assert.Equal("token_expired", await Problems.ReadCodeAsync(beyondSkew));
    }

    public static TheoryData<string> RefusedTokens()
    {
        var claims = new Dictionary<string, object>
        {
            ["sub"] = Guid.NewGuid().ToString(),
            ["merchant_id"] = Guid.NewGuid().ToString(),
            ["jti"] = "x",
        };
        var otherKey = Enumerable.Repeat((byte)7, 64).ToArray();
        var genuine = Tokens.Forge(claims);
        var parts = genuine.Split('.');
        var bentPayload = Base64UrlEncoder.Encode(Base64UrlEncoder.Decode(parts[1]).Replace("\"jti\":\"x\"", "\"jti\":\"y\"", StringComparison.Ordinal));
        return new TheoryData<string>
        {
            { "not-a-jwt" },
            { Tokens.Forge(claims, key: otherKey) },
            { $"{parts[0]}.{bentPayload}.{parts[2]}" },
            { Tokens.WithoutSignature(claims) },
            { Tokens.Forge(claims, algorithm: SecurityAlgorithms.HmacSha512) },
            { Tokens.Forge(claims, issuer: "someone-else") },
            { Tokens.Forge(claims, audience: "another-api") },
            { Tokens.Forge(claims, issuedAt: TimeProvider.System.GetUtcNow().AddMinutes(10)) },
        };
    }

    [Theory]
    [MemberData(nameof(RefusedTokens))]
    public async Task A_malformed_forged_or_bent_token_is_401_invalid_token(string token)
    {
        using var response = await CallWithTokenAsync(token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Bearer error=\"invalid_token\"", response.Headers.WwwAuthenticate.ToString());
        Assert.Equal("unauthorized", await Problems.ReadCodeAsync(response));
    }

    [Theory]
    [InlineData("sub")]
    [InlineData("merchant_id")]
    public async Task A_genuine_token_without_both_ids_is_403(string missing)
    {
        var user = await Merchants.CreateUserAsync(Factory, $"partial-{missing}@example.com");
        var claims = Tokens.ClaimsFor(user);
        claims.Remove(missing);

        using var response = await CallWithTokenAsync(Tokens.Forge(claims));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_genuine_token_for_a_user_who_no_longer_exists_is_401()
    {
        var ghost = new TestUser(new(Guid.NewGuid()), new(Guid.NewGuid()), "ghost@example.com", "x", "Nobody");

        using var response = await CallWithTokenAsync(Tokens.Forge(Tokens.ClaimsFor(ghost)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthorized", await Problems.ReadCodeAsync(response));
    }

    [Fact]
    public async Task A_token_naming_another_merchant_finds_no_one()
    {
        // The user exists, but the token claims a merchant they do not belong to: the merchant id is taken from
        // the token and checked, never trusted on its own.
        var user = await Merchants.CreateUserAsync(Factory, "scoped@example.com");
        var other = await Merchants.CreateUserAsync(Factory, "other-owner@example.com", merchantName: "Other Shop");
        var claims = Tokens.ClaimsFor(user);
        claims["merchant_id"] = other.MerchantId.ToString();

        using var response = await CallWithTokenAsync(Tokens.Forge(claims));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<HttpResponseMessage> CallWithTokenAsync(string token)
    {
        using var client = Factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, ProtectedPath);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return await client.SendAsync(request, Cancellation);
    }
}
