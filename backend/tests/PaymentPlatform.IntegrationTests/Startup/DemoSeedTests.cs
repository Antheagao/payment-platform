using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application.Demo;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Payments;
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
        Assert.Equal(DemoPayments.All.Count, await db.Payments.CountAsync(payment => payment.MerchantId == DemoSeed.MerchantId, Cancellation));
        Assert.Equal(
            DemoPayments.All.Count,
            await db.PaymentEvents.Where(row => row.ToStatus == PaymentStatus.Pending).CountAsync(Cancellation));
    }

    [Fact]
    public async Task The_sample_payments_cover_every_status_and_every_history_is_a_legal_path()
    {
        await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting(DemoSeed.SettingName, "true"));
        using var client = factory.CreateClient();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var payments = await db.Payments.AsNoTracking().Where(payment => payment.MerchantId == DemoSeed.MerchantId).ToListAsync(Cancellation);
        var events = (await db.PaymentEvents.AsNoTracking().ToListAsync(Cancellation)).ToLookup(row => row.PaymentId);

        Assert.Equal(Enum.GetValues<PaymentStatus>().Order(), payments.Select(payment => payment.Status).Distinct().Order());
        var now = TimeProvider.System.GetUtcNow();
        foreach (var payment in payments)
        {
            var history = events[payment.Id].OrderBy(row => row.Id).ToList();
            Assert.Null(history[0].FromStatus);
            Assert.Equal(PaymentStatus.Pending, history[0].ToStatus);
            for (var step = 1; step < history.Count; step++)
            {
                Assert.Equal(history[step - 1].ToStatus, history[step].FromStatus);
                Assert.True(
                    PaymentLifecycle.CanTransition(history[step].FromStatus!.Value, history[step].ToStatus),
                    $"{payment.Id}: {history[step].FromStatus} to {history[step].ToStatus}");
                Assert.True(history[step].OccurredAt > history[step - 1].OccurredAt);
            }

            Assert.Equal(payment.Status, history[^1].ToStatus);
            Assert.All(history, row => Assert.Equal(DemoPayments.CorrelationId, row.CorrelationId));
            Assert.All(history, row => Assert.True(row.OccurredAt <= now, $"{payment.Id} has an event in the future"));
            Assert.All(
                history.Where(row => row.ToStatus == PaymentStatus.Settled),
                row => Assert.Equal((ActorKind.System, "settlement"), (row.ActorKind, row.ActorId)));
            Assert.InRange(payment.CreatedAt, now.AddDays(-90), now);
        }
    }

    [Fact]
    public async Task A_seeded_payment_reads_back_through_the_api_with_its_whole_trail()
    {
        await using var factory = Factory.WithWebHostBuilder(builder => builder.UseSetting(DemoSeed.SettingName, "true"));
        using var client = factory.CreateClient();
        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login", new { email = DemoSeed.Email, password = DemoSeed.Password }, Cancellation);
        using var token = System.Text.Json.JsonDocument.Parse(await login.Content.ReadAsStringAsync(Cancellation));
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", token.RootElement.GetProperty("access_token").GetString());

        using var response = await client.GetAsync($"/api/v1/payments/{DemoPayments.IdOf(4)}", Cancellation);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync(Cancellation));
        Assert.Equal("refunded", body.RootElement.GetProperty("status").GetString());
        Assert.Equal("INV-1004", body.RootElement.GetProperty("merchant_reference").GetString());
        Assert.Equal(
            ["pending", "authorized", "captured", "settled", "refunded"],
            body.RootElement.GetProperty("history").EnumerateArray().Select(entry => entry.GetProperty("to").GetString()));
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
