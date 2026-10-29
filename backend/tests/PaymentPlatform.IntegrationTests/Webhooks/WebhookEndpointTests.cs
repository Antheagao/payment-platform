using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application.Webhooks;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Webhooks;

/// <summary>
/// <c>GET</c> and <c>PUT /api/v1/webhook-endpoint</c> and <c>POST .../rotate-secret</c>: one endpoint per merchant,
/// its secret shown once, and the SSRF rules on the URL.
/// </summary>
public sealed class WebhookEndpointTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string Path = "/api/v1/webhook-endpoint";
    private const string Url = $"https://{FakeHostResolver.PublicHost}/payments";

    private WebApplicationFactory<Program>? _api;

    // The real API with names answered by the fake resolver; disposed with the factory it derives from.
    private WebApplicationFactory<Program> Api => _api ??= Factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => services.AddSingleton<IHostResolver, FakeHostResolver>()));

    [Fact]
    public async Task A_merchant_with_no_endpoint_gets_404_from_read_and_rotate()
    {
        using var client = await ClientAsync("none@example.com");

        using var read = await client.GetAsync(Path, Cancellation);
        using var rotate = await client.PostAsync($"{Path}/rotate-secret", null, Cancellation);

        Assert.Equal("webhook_endpoint_not_found", await Problems.ReadCodeAsync(read));
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Equal("webhook_endpoint_not_found", await Problems.ReadCodeAsync(rotate));
        Assert.Equal(HttpStatusCode.NotFound, rotate.StatusCode);
    }

    [Fact]
    public async Task The_first_set_creates_the_endpoint_and_shows_its_secret_once()
    {
        var (client, user) = await ClientAndUserAsync("first@example.com");
        using (client)
        {
            using var created = await client.PutAsJsonAsync(Path, new { url = Url }, Cancellation);
            using var read = await client.GetAsync(Path, Cancellation);

            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            Assert.Equal(Path, created.Headers.Location?.OriginalString);
            var body = await JsonAsync(created);
            var secret = body.GetProperty("secret").GetString()!;
            Assert.Matches("^whsec_[A-Za-z0-9_-]{43}$", secret);
            Assert.Equal(Url, body.GetProperty("url").GetString());
            Assert.True(body.GetProperty("enabled").GetBoolean());

            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
            var shown = await JsonAsync(read);
            Assert.False(shown.TryGetProperty("secret", out _));
            Assert.Equal(Url, shown.GetProperty("url").GetString());
            Assert.Equal(secret, (await StoredAsync(user)).Secret);
        }
    }

    [Fact]
    public async Task A_later_set_changes_the_url_and_switch_and_keeps_the_secret()
    {
        var (client, user) = await ClientAndUserAsync("later@example.com");
        using (client)
        {
            using var created = await client.PutAsJsonAsync(Path, new { url = Url }, Cancellation);
            var secret = (await JsonAsync(created)).GetProperty("secret").GetString();

            using var moved = await client.PutAsJsonAsync(Path, new { url = $"{Url}/v2", enabled = false }, Cancellation);
            var movedBody = await JsonAsync(moved);
            Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
            Assert.False(movedBody.TryGetProperty("secret", out _));
            Assert.False(movedBody.GetProperty("enabled").GetBoolean());
            Assert.Equal($"{Url}/v2", movedBody.GetProperty("url").GetString());

            using var again = await client.PutAsJsonAsync(Path, new { url = $"{Url}/v2" }, Cancellation);
            Assert.True((await JsonAsync(again)).GetProperty("enabled").GetBoolean());

            var stored = await StoredAsync(user);
            Assert.Equal(secret, stored.Secret);
            Assert.True(stored.Enabled);
            Assert.True(stored.UpdatedAt > stored.CreatedAt);
        }
    }

    [Fact]
    public async Task Rotating_issues_a_new_secret_and_shows_it_once()
    {
        var (client, user) = await ClientAndUserAsync("rotate@example.com");
        using (client)
        {
            using var created = await client.PutAsJsonAsync(Path, new { url = Url }, Cancellation);
            var first = (await JsonAsync(created)).GetProperty("secret").GetString();

            using var rotated = await client.PostAsync($"{Path}/rotate-secret", null, Cancellation);
            using var read = await client.GetAsync(Path, Cancellation);

            Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
            var second = (await JsonAsync(rotated)).GetProperty("secret").GetString();
            Assert.NotEqual(first, second);
            Assert.Matches("^whsec_[A-Za-z0-9_-]{43}$", second!);
            Assert.Equal(second, (await StoredAsync(user)).Secret);
            Assert.False((await JsonAsync(read)).TryGetProperty("secret", out _));
        }
    }

    [Fact]
    public async Task One_merchant_never_sees_or_changes_another_merchants_endpoint()
    {
        var (owner, ownerUser) = await ClientAndUserAsync("owner@example.com");
        using var other = await ClientAsync("other@example.com", merchantName: "Other");
        using (owner)
        {
            using var created = await owner.PutAsJsonAsync(Path, new { url = Url }, Cancellation);
            var secret = (await JsonAsync(created)).GetProperty("secret").GetString();

            using var read = await other.GetAsync(Path, Cancellation);
            using var rotate = await other.PostAsync($"{Path}/rotate-secret", null, Cancellation);
            using var set = await other.PutAsJsonAsync(Path, new { url = $"{Url}/other" }, Cancellation);

            Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, rotate.StatusCode);
            Assert.Equal(HttpStatusCode.Created, set.StatusCode);
            var stored = await StoredAsync(ownerUser);
            Assert.Equal((Url, secret), (stored.Url, stored.Secret));
        }
    }

    [Theory]
    [InlineData("GET", Path)]
    [InlineData("PUT", Path)]
    [InlineData("POST", $"{Path}/rotate-secret")]
    public async Task Every_call_needs_a_token(string method, string path)
    {
        using var client = Api.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { url = Url }) };

        using var response = await client.SendAsync(request, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(null, WebhookUrlPolicy.Required)]
    [InlineData("not a url", WebhookUrlPolicy.Invalid)]
    [InlineData("https://user:secret@hooks.example.com/payments", WebhookUrlPolicy.Invalid)]
    [InlineData("http://hooks.example.com/payments", WebhookUrlPolicy.NotHttps)]
    [InlineData("https://127.0.0.1/payments", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://169.254.169.254/latest/meta-data", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://localhost:5080/api/v1/payments", WebhookUrlPolicy.NotPublic)]
    [InlineData($"https://{FakeHostResolver.PrivateHost}/payments", WebhookUrlPolicy.NotPublic)]
    [InlineData("https://nowhere.example.com/payments", WebhookUrlPolicy.Unresolvable)]
    public async Task A_url_that_breaks_a_rule_is_a_field_error_and_nothing_is_stored(string? url, string code)
    {
        var (client, user) = await ClientAndUserAsync($"bad-{Guid.NewGuid():N}@example.com");
        using (client)
        {
            using var response = await client.PutAsJsonAsync(Path, new { url }, Cancellation);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var errors = (await JsonAsync(response)).GetProperty("errors").GetProperty("url");
            Assert.Equal(code, Assert.Single(errors.EnumerateArray()).GetString());
            await using var scope = Api.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            Assert.False(await db.WebhookEndpoints.AnyAsync(endpoint => endpoint.MerchantId == user.MerchantId, Cancellation));
        }
    }

    [Fact]
    public async Task Concurrent_first_sets_make_one_endpoint_and_one_secret()
    {
        // Six at once all succeed and leave one endpoint. Whether two of them collide depends on timing; the test
        // below makes the collision happen every time.
        const int requests = 6;
        var user = await Merchants.CreateUserAsync(Api, "racing@example.com");
        using var client = Clients.For(Api, user);

        var responses = await Task.WhenAll(Enumerable.Range(0, requests)
            .Select(index => client.PutAsJsonAsync(Path, new { url = $"{Url}/{index}" }, Cancellation)));
        try
        {
            Assert.All(responses, response => Assert.True(response.IsSuccessStatusCode, response.StatusCode.ToString()));
            var created = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var secret = (await JsonAsync(created)).GetProperty("secret").GetString();
            Assert.Equal(secret, (await StoredAsync(user)).Secret);
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
    public async Task A_set_that_loses_the_race_to_create_becomes_an_update_of_the_winner()
    {
        // Another request creates the endpoint between this one's read and its save, every time.
        var rival = new RivalInsert();
        await using var raced = Api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<PaymentsDbContext>(options => options.AddInterceptors(rival))));
        var user = await Merchants.CreateUserAsync(raced, "lost-race@example.com");
        rival.MerchantId = user.MerchantId.Value;
        using var client = Clients.For(raced, user);

        using var response = await client.PutAsJsonAsync(Path, new { url = Url }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await JsonAsync(response)).TryGetProperty("secret", out _));
        await using var scope = raced.Services.CreateAsyncScope();
        var stored = await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().WebhookEndpoints.AsNoTracking()
            .SingleAsync(endpoint => endpoint.MerchantId == user.MerchantId, Cancellation);
        Assert.Equal((Url, RivalInsert.Secret), (stored.Url, stored.Secret));
    }

    [Fact]
    public async Task No_secret_reaches_a_log_line_and_the_url_is_logged_by_host_only()
    {
        var logged = LogCapture.Attach(Api);
        var user = await Merchants.CreateUserAsync(logged, "logged@example.com");
        using var client = Clients.For(logged, user);

        using var created = await client.PutAsJsonAsync(Path, new { url = $"{Url}?token=merchant-own-token" }, Cancellation);
        using var rotated = await client.PostAsync($"{Path}/rotate-secret", null, Cancellation);
        var secrets = new[]
        {
            (await JsonAsync(created)).GetProperty("secret").GetString()!,
            (await JsonAsync(rotated)).GetProperty("secret").GetString()!,
        };

        var lines = LogCapture.Lines(logged);
        Assert.Contains(lines, line => line.Contains(FakeHostResolver.PublicHost, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("merchant-own-token", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => secrets.Any(secret => line.Contains(secret, StringComparison.Ordinal)));
        Assert.DoesNotContain(lines, line => line.Contains("whsec_", StringComparison.Ordinal));
    }

    [Fact]
    public void A_local_receiver_list_is_refused_outside_development()
    {
        using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Webhooks:LocalReceiverHosts:0", "webhook-receiver");
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains("LocalReceiverHosts", error.Message, StringComparison.Ordinal);
    }

    private async Task<HttpClient> ClientAsync(string email, string merchantName = "Test Merchant") =>
        (await ClientAndUserAsync(email, merchantName)).Client;

    private async Task<(HttpClient Client, TestUser User)> ClientAndUserAsync(string email, string merchantName = "Test Merchant")
    {
        var user = await Merchants.CreateUserAsync(Api, email, merchantName: merchantName);
        return (Clients.For(Api, user), user);
    }

    private async Task<WebhookEndpoint> StoredAsync(TestUser user)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().WebhookEndpoints.AsNoTracking()
            .SingleAsync(endpoint => endpoint.MerchantId == user.MerchantId, Cancellation);
    }

    /// <summary>Before the first save that adds an endpoint, inserts one for the same merchant on its own connection.</summary>
    private sealed class RivalInsert : SaveChangesInterceptor
    {
        public const string Secret = "whsec_rival";

        private int _fired;

        public Guid MerchantId { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            var context = eventData.Context!;
            if (context.ChangeTracker.Entries<WebhookEndpoint>().Any(entry => entry.State == EntityState.Added)
                && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await using var connection = new SqlConnection(context.Database.GetConnectionString());
                await connection.OpenAsync(cancellationToken);
                await using var insert = connection.CreateCommand();
                insert.CommandText = """
                    INSERT payments.WebhookEndpoints (Id, MerchantId, Url, Secret, Enabled, CreatedAt, UpdatedAt)
                    VALUES (NEWID(), @merchant, N'https://hooks.example.com/rival', @secret, 1, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET())
                    """;
                insert.Parameters.AddWithValue("@merchant", MerchantId);
                insert.Parameters.AddWithValue("@secret", Secret);
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation)).RootElement;
}
