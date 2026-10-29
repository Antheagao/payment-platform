using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.IntegrationTests.Support;

namespace PaymentPlatform.IntegrationTests.Startup;

/// <summary>
/// The JWT signing key is a secret with no committed default: outside Development the API will not start without
/// a sound one, and in Development it makes its own so the stack still starts with no setup.
/// </summary>
public sealed class SigningKeyTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Theory]
    [InlineData("")]
    [InlineData("not base64 at all!")]
    [InlineData("c2hvcnQta2V5")]
    public async Task Outside_development_a_missing_malformed_or_short_key_stops_startup(string key)
    {
        // A host with the API's own authentication setup, started directly. Through WebApplicationFactory the
        // failure races the factory: Program's RunAsync disposes the failed host while the factory still reads its
        // services, so the test sometimes saw an ObjectDisposedException instead of the reason (a CI run did).
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Configuration["Jwt:SigningKey"] = key;
        builder.AddApiAuthentication();
        await using var app = builder.Build();

        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => app.StartAsync(Cancellation));

        Assert.Contains("Jwt:SigningKey", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task In_development_a_missing_key_is_generated_and_logins_work()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("Jwt:SigningKey", "");
        });
        var user = await Merchants.CreateUserAsync(factory, "dev@example.com");
        using var client = factory.CreateClient();

        using var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email = user.Email, password = user.Password }, Cancellation);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }
}
