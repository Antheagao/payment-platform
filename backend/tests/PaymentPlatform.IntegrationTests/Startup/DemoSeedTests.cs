using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application.Demo;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Startup;

/// <summary>The demo data compose loads (<c>Demo:Seed</c>): present, idempotent, and refused in Production.</summary>
public sealed class DemoSeedTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task The_demo_login_works_and_seeding_on_every_start_adds_nothing()
    {
        for (var start = 0; start < 2; start++)
        {
            await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting(DemoSeed.SettingName, "true"));
            using var client = factory.CreateClient();

            using var login = await client.PostAsJsonAsync(
                "/api/v1/auth/login", new { email = DemoSeed.Email, password = DemoSeed.Password }, Cancellation);

            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        }

        await using var scope = Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        Assert.Equal(1, await db.Merchants.CountAsync(merchant => merchant.Id == DemoSeed.MerchantId, Cancellation));
        Assert.Equal(1, await db.MerchantUsers.CountAsync(user => user.Email == DemoSeed.Email, Cancellation));
    }

    [Fact]
    public async Task Without_the_setting_there_is_no_demo_login()
    {
        using var client = Factory.CreateClient();

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = DemoSeed.Email, password = DemoSeed.Password }, Cancellation);

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Production_refuses_to_start_with_the_demo_seed_on()
    {
        await using var factory = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseSetting(DemoSeed.SettingName, "true");
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        Assert.Contains(DemoSeed.SettingName, error.Message, StringComparison.Ordinal);
    }
}
