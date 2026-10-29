using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace PaymentPlatform.IntegrationTests.Support;

public static class Clients
{
    /// <summary>A client that sends a valid token for <paramref name="user"/>, as the dashboard's server would.</summary>
    public static HttpClient For(WebApplicationFactory<Program> factory, TestUser user)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Tokens.Forge(Tokens.ClaimsFor(user)));
        return client;
    }
}
