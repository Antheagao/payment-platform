using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Telemetry;

/// <summary>
/// `Error` means a person should look (ADR 005): a conflict the API handles as designed, such as a retried request
/// meeting its own idempotency key, is no error; a database failure nothing handles is one, logged once.
/// </summary>
public sealed class ErrorLogTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    [Fact]
    public async Task Retries_and_refused_moves_log_nothing_at_error()
    {
        await using var factory = WithLogs(_ => { });
        var user = await Merchants.CreateUserAsync(factory, "quiet-retries@example.com");
        using var client = Clients.For(factory, user);
        var body = CreatePaymentTests.Body("4242424242424242");

        using var created = await PaymentRequests.CreateAsync(client, body, "same-key");
        using var replayed = await PaymentRequests.CreateAsync(client, body, "same-key");
        using var reused = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242", amount: 7), "same-key");
        var id = (await created.Content.ReadFromJsonAsync<Created>(Cancellation))!.Id;
        using var captured = await PaymentRequests.MoveAsync(client, id, "capture", "capture-key");
        using var capturedAgain = await PaymentRequests.MoveAsync(client, id, "capture", "capture-key");
        using var secondCapture = await PaymentRequests.MoveAsync(client, id, "capture", "another-key");

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.UnprocessableEntity, HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.Conflict],
            new[] { created, replayed, reused, captured, capturedAgain, secondCapture }.Select(response => response.StatusCode));
        Assert.Empty(factory.Services.GetFakeLogCollector().GetSnapshot().Where(record => record.Level >= LogLevel.Error)
            .Select(record => $"{record.Category}: {record.Message}"));
    }

    [Fact]
    public async Task A_database_failure_nothing_handles_is_logged_at_error()
    {
        await using var factory = WithLogs(services =>
            services.ConfigureDbContext<PaymentsDbContext>(options => options.AddInterceptors(new FailPaymentInserts())));
        var user = await Merchants.CreateUserAsync(factory, "loud-failure@example.com");
        using var client = Clients.For(factory, user);

        using var response = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body("4242424242424242"), "failing-key");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var errors = factory.Services.GetFakeLogCollector().GetSnapshot().Where(record => record.Level >= LogLevel.Error).ToList();
        Assert.Contains(errors, record => record.Exception is DbUpdateException);
    }

    private WebApplicationFactory<Program> WithLogs(Action<IServiceCollection> services) =>
        Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddFakeLogging();
            });
            builder.ConfigureTestServices(services);
        });

    private sealed record Created(Guid Id);

    /// <summary>Fails any save that inserts a payment, as a database the API cannot handle would.</summary>
    private sealed class FailPaymentInserts : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<Payment>().Any(entry => entry.State == EntityState.Added))
            {
                throw new DbUpdateException("injected: the database refused the payment");
            }

            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
