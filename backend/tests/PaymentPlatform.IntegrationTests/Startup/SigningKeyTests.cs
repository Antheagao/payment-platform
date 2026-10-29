using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
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
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("Jwt:SigningKey", key);
        });

        var error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("Jwt:SigningKey", Unwrap(error).Message, StringComparison.Ordinal);
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

    private static Exception Unwrap(Exception error) =>
        error is OptionsValidationException or InvalidOperationException || error.InnerException is null
            ? error
            : Unwrap(error.InnerException);
}
