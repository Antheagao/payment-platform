using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Auth;

public sealed class LoginTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string LoginPath = "/api/v1/auth/login";

    [Fact]
    public async Task A_correct_password_returns_a_30_minute_hs256_token_for_the_user_and_merchant()
    {
        var user = await Merchants.CreateUserAsync(Factory, "owner@example.com", merchantName: "Corner Cafe");
        using var client = Factory.CreateClient();
        var before = TimeProvider.System.GetUtcNow();

        using var response = await LogInAsync(client, user.Email, user.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        var root = body.RootElement;
        Assert.Equal("Bearer", root.GetProperty("token_type").GetString());
        Assert.Equal(1800, root.GetProperty("expires_in").GetInt32());
        var expiresAt = root.GetProperty("expires_at").GetDateTimeOffset();
        Assert.InRange(expiresAt, before.AddMinutes(30).AddSeconds(-1), TimeProvider.System.GetUtcNow().AddMinutes(30).AddSeconds(1));

        var token = new JsonWebToken(root.GetProperty("access_token").GetString());
        Assert.Equal("HS256", token.Alg);
        Assert.Equal(Tokens.Issuer, token.Issuer);
        Assert.Equal([Tokens.Audience], token.Audiences);
        Assert.Equal(user.UserId.ToString(), token.Subject);
        Assert.Equal(user.MerchantId.ToString(), token.GetClaim("merchant_id").Value);
        Assert.False(string.IsNullOrEmpty(token.Id));
        Assert.Equal(TimeSpan.FromMinutes(30), token.ValidTo - token.IssuedAt);
        // Nothing personal rides in the token: the email stays on the server.
        Assert.DoesNotContain(token.Claims, claim => claim.Value.Contains(user.Email, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task The_issued_token_opens_the_protected_api_and_names_the_user()
    {
        var user = await Merchants.CreateUserAsync(Factory, "reader@example.com", merchantName: "Book Nook");
        using var client = Factory.CreateClient();
        var token = await LogInForTokenAsync(client, user.Email, user.Password);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal(user.UserId.Value, body.RootElement.GetProperty("user_id").GetGuid());
        Assert.Equal(user.MerchantId.Value, body.RootElement.GetProperty("merchant_id").GetGuid());
        Assert.Equal(user.Email, body.RootElement.GetProperty("email").GetString());
        Assert.Equal("Book Nook", body.RootElement.GetProperty("merchant_name").GetString());
    }

    [Fact]
    public async Task The_email_matches_without_regard_to_case_or_surrounding_space()
    {
        var user = await Merchants.CreateUserAsync(Factory, "casey@example.com");
        using var client = Factory.CreateClient();

        using var response = await LogInAsync(client, "  Casey@EXAMPLE.com ", user.Password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_email_get_the_same_answer()
    {
        var user = await Merchants.CreateUserAsync(Factory, "same@example.com");
        using var client = Factory.CreateClient();

        using var wrongPassword = await LogInAsync(client, user.Email, "not the password");
        using var unknownEmail = await LogInAsync(client, "nobody@example.com", user.Password);

        foreach (var response in new[] { wrongPassword, unknownEmail })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
        }

        using var first = await Problems.ReadAsync(wrongPassword);
        using var second = await Problems.ReadAsync(unknownEmail);
        foreach (var field in new[] { "code", "title", "detail", "status", "type" })
        {
            Assert.Equal(first.RootElement.GetProperty(field).ToString(), second.RootElement.GetProperty(field).ToString());
        }

        Assert.Equal("invalid_credentials", first.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task An_unknown_email_costs_one_password_verification_like_a_known_one()
    {
        var counter = new CountingHasher();
        await using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IPasswordHasher<MerchantUser>>(counter)));
        var user = await Merchants.CreateUserAsync(factory, "timing@example.com");
        using var client = factory.CreateClient();

        counter.Verifications = 0;
        using var known = await LogInAsync(client, user.Email, "wrong");
        var forKnown = counter.Verifications;
        counter.Verifications = 0;
        using var unknown = await LogInAsync(client, "ghost@example.com", "wrong");

        Assert.Equal(1, forKnown);
        Assert.Equal(1, counter.Verifications);
    }

    public static TheoryData<string, string, string> InvalidBodies() => new()
    {
        { """{}""", "email", "email_required" },
        { """{"password":"x"}""", "email", "email_required" },
        { """{"email":"   ","password":"x"}""", "email", "email_required" },
        { """{"email":"not-an-email","password":"x"}""", "email", "email_invalid" },
        { """{"email":"a@example.com"}""", "password", "password_required" },
        { """{"email":"a@example.com","password":""}""", "password", "password_required" },
        { $$"""{"email":"a@example.com","password":"{{new string('p', 129)}}"}""", "password", "password_too_long" },
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task An_incomplete_request_is_a_400_with_a_code_for_each_field(string json, string field, string code)
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsync(LoginPath, new StringContent(json, Encoding.UTF8, "application/json"), Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await Problems.ReadAsync(response);
        Assert.Equal("invalid_request", problem.RootElement.GetProperty("code").GetString());
        Assert.Contains(code, problem.RootElement.GetProperty("errors").GetProperty(field).EnumerateArray().Select(error => error.GetString()));
    }

    [Theory]
    [InlineData("""{"email":""", "application/json")]
    [InlineData("", "application/json")]
    [InlineData("""{"email":"a@example.com","password":"x"}""", "text/plain")]
    public async Task A_body_that_does_not_parse_is_a_problem_not_a_crash(string body, string contentType)
    {
        using var client = Factory.CreateClient();

        using var response = await client.PostAsync(LoginPath, new StringContent(body, Encoding.UTF8, contentType), Cancellation);

        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnsupportedMediaType,
            $"expected 400 or 415, got {(int)response.StatusCode}");
        using var problem = await Problems.ReadAsync(response);
        var code = problem.RootElement.GetProperty("code").GetString();
        Assert.True(code is "invalid_request" or "unsupported_media_type", $"unexpected code {code}");
        Assert.False(string.IsNullOrEmpty(problem.RootElement.GetProperty("detail").GetString()));
    }

    [Fact]
    public async Task Too_many_attempts_for_one_email_are_429_with_retry_after_and_other_emails_are_untouched()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:LoginRateLimit:PerEmailPermits", "3"));
        var user = await Merchants.CreateUserAsync(factory, "target@example.com");
        var bystander = await Merchants.CreateUserAsync(factory, "bystander@example.com");
        using var client = factory.CreateClient();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            using var allowed = await LogInAsync(client, user.Email, "guess");
            Assert.Equal(HttpStatusCode.Unauthorized, allowed.StatusCode);
        }

        // The right password does not get past the limit either, and the limit ignores case.
        using var limited = await LogInAsync(client, "TARGET@example.com", user.Password);
        using var other = await LogInAsync(client, bystander.Email, bystander.Password);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.True(int.Parse(limited.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.Equal("rate_limited", await Problems.ReadCodeAsync(limited));
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
    }

    [Fact]
    public async Task Too_many_attempts_from_one_address_are_429_with_retry_after()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Auth:LoginRateLimit:PerAddressPermits", "2"));
        using var client = factory.CreateClient();

        using var first = await LogInAsync(client, "one@example.com", "x");
        using var second = await LogInAsync(client, "two@example.com", "x");
        using var third = await LogInAsync(client, "three@example.com", "x");

        Assert.Equal(HttpStatusCode.Unauthorized, second.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, third.StatusCode);
        Assert.True(int.Parse(third.Headers.GetValues("Retry-After").Single(), System.Globalization.CultureInfo.InvariantCulture) >= 1);
        using var problem = await Problems.ReadAsync(third);
        Assert.Equal("rate_limited", problem.RootElement.GetProperty("code").GetString());
        Assert.Equal("https://www.rfc-editor.org/rfc/rfc6585#section-4", problem.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task A_stored_hash_weaker_than_the_hasher_is_upgraded_at_the_next_login()
    {
        var weak = new PasswordHasher<MerchantUser>(Options.Create(new PasswordHasherOptions { IterationCount = 1_000 }));
        const string password = "an older account";
        var user = await Merchants.CreateUserAsync(Factory, "legacy@example.com", password, passwordHash: weak.HashPassword(null!, password));
        using var client = Factory.CreateClient();

        using var response = await LogInAsync(client, user.Email, password);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var scope = Factory.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().MerchantUsers
            .AsNoTracking().SingleAsync(row => row.Id == user.UserId, Cancellation);
        var current = scope.ServiceProvider.GetRequiredService<IPasswordHasher<MerchantUser>>();
        Assert.Equal(PasswordVerificationResult.Success, current.VerifyHashedPassword(stored, stored.PasswordHash, password));
    }

    [Fact]
    public async Task No_password_email_or_token_reaches_the_logs()
    {
        await using var factory = Factory.WithWebHostBuilder(builder => builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            logging.AddFakeLogging();
        }));
        var user = await Merchants.CreateUserAsync(factory, "private.person@example.com", "s3cret-passphrase");
        using var client = factory.CreateClient();

        var token = await LogInForTokenAsync(client, user.Email, user.Password);
        using var wrong = await LogInAsync(client, user.Email, "another-secret-guess");
        using var unknown = await LogInAsync(client, "unknown.person@example.com", "third-secret-guess");

        var lines = factory.Services.GetFakeLogCollector().GetSnapshot()
            .Select(record => $"{record.Message} {string.Join(' ', record.StructuredState?.Select(pair => pair.Value) ?? [])}")
            .ToList();
        Assert.NotEmpty(lines);
        foreach (var secret in new[] { user.Password, "another-secret-guess", "third-secret-guess", "private.person", "unknown.person", token })
        {
            Assert.DoesNotContain(lines, line => line.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }

    private static Task<HttpResponseMessage> LogInAsync(HttpClient client, string email, string password) =>
        client.PostAsJsonAsync(LoginPath, new { email, password }, Cancellation);

    private static async Task<string> LogInForTokenAsync(HttpClient client, string email, string password)
    {
        using var response = await LogInAsync(client, email, password);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        return body.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <summary>The real hasher, counting how often a login checks a password.</summary>
    private sealed class CountingHasher : IPasswordHasher<MerchantUser>
    {
        private readonly PasswordHasher<MerchantUser> _inner = new();

        public int Verifications { get; set; }

        public string HashPassword(MerchantUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(MerchantUser user, string hashedPassword, string providedPassword)
        {
            Verifications++;
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }
}
