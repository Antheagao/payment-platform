using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Payments;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Payments;

/// <summary>
/// <c>ReconcilePendingPayments</c>: payments the acquirer did not answer for during create are asked about again
/// once past the grace period, moved on with their audit row and event, asked again later while still unanswered,
/// and failed once they have waited too long; the stuck ones never hold up the rest.
/// </summary>
public sealed class ReconciliationTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private const string LateCard = "4000000000000127";
    private const string SilentCard = "4000000000000119";
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromDays(1);

    /// <summary>How long the acquirer takes to answer, on the fake clock, so a run ends later than it starts.</summary>
    private static readonly TimeSpan AcquirerLatency = TimeSpan.FromSeconds(1);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
    private WebApplicationFactory<Program>? _api;
    private int _batchSize = 50;

    private WebApplicationFactory<Program> Api => _api ??= Factory.WithWebHostBuilder(builder =>
    {
        builder.UseSetting("Reconciliation:GracePeriod", Grace.ToString());
        builder.UseSetting("Reconciliation:RetryInterval", RetryInterval.ToString());
        builder.UseSetting("Reconciliation:GiveUpAfter", GiveUpAfter.ToString());
        builder.UseSetting("Reconciliation:BatchSize", _batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureLogging(logging => logging.AddFakeLogging());
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(_clock);
            services.AddSingleton<IAcquirer>(provider => new SlowAcquirer(
                new SimulatedAcquirer(provider.GetRequiredService<IOptions<SimulatedAcquirerOptions>>(), _clock), _clock));
        });
    });

    [Fact]
    public async Task A_late_answer_is_authorized_once_past_the_grace_period_with_its_audit_row_and_event()
    {
        var user = await Merchants.CreateUserAsync(Api, "reconcile-late@example.com");
        var late = await CreateAsync(user, LateCard);
        var authorized = await CreateAsync(user, "4242424242424242");

        var early = await ReconcileAsync();
        _clock.Advance(Grace + TimeSpan.FromSeconds(1));
        var run = await ReconcileAsync();

        Assert.Equal(0, early.Count);
        Assert.Equal(new ReconciliationRun(Authorized: 1, Failed: 0, StillPending: 0, TakenElsewhere: 0), run);
        Assert.Equal(PaymentStatus.Authorized, await StatusAsync(late));
        Assert.Equal(PaymentStatus.Authorized, await StatusAsync(authorized));

        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var change = await db.PaymentEvents.AsNoTracking().Where(row => row.PaymentId == late).OrderBy(row => row.Id).LastAsync(Cancellation);
        Assert.Equal(
            (PaymentStatus.Pending, PaymentStatus.Authorized, ActorKind.System, ReconcilePendingPayments.ActorName),
            (change.FromStatus!.Value, change.ToStatus, change.ActorKind, change.ActorId));
        Assert.StartsWith("reconciliation-", change.CorrelationId, StringComparison.Ordinal);
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.PaymentId == late, Cancellation);
        Assert.Equal(("payment.authorized", change.CorrelationId), (message.EventType, message.CorrelationId));
    }

    [Fact]
    public async Task A_payment_never_answered_for_is_asked_again_after_the_interval_and_fails_after_a_day()
    {
        var user = await Merchants.CreateUserAsync(Api, "reconcile-silent@example.com");
        var silent = await CreateAsync(user, SilentCard);
        _clock.Advance(Grace + TimeSpan.FromSeconds(1));

        var first = await ReconcileAsync();
        var straightAway = await ReconcileAsync();
        // A run starting a moment short of one interval after the first, though the acquirer took a second to
        // answer that one, still finds it due: runs drift by milliseconds from one minute to the next.
        _clock.Advance(RetryInterval - AcquirerLatency - TimeSpan.FromSeconds(1));
        var afterInterval = await ReconcileAsync();

        Assert.Equal(1, first.StillPending);
        Assert.Equal(0, straightAway.Count);
        Assert.Equal(1, afterInterval.StillPending);
        Assert.Equal(PaymentStatus.Pending, await StatusAsync(silent));
        // Nothing was recorded for a payment that did not change: no audit row past its creation, no event.
        Assert.Equal(1, await CountAsync(db => db.PaymentEvents.CountAsync(row => row.PaymentId == silent, Cancellation)));

        _clock.Advance(GiveUpAfter);
        var gaveUp = await ReconcileAsync();

        Assert.Equal(1, gaveUp.Failed);
        Assert.Equal(PaymentStatus.Failed, await StatusAsync(silent));
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var payment = await db.Payments.AsNoTracking().SingleAsync(row => row.Id == silent, Cancellation);
        Assert.Equal(ReconcilePendingPayments.UnreachableCode, payment.FailureReason);
        var message = await db.OutboxMessages.AsNoTracking().SingleAsync(row => row.PaymentId == silent, Cancellation);
        Assert.Equal("payment.failed", message.EventType);
        // Giving up needs a person to check with the acquirer, so it is logged as an error (ADR 005).
        Assert.Contains(Api.Services.GetFakeLogCollector().GetSnapshot(), record =>
            record.Level == LogLevel.Error && record.Message.Contains("acquirer_unreachable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Payments_still_unanswered_wait_their_interval_so_newer_ones_are_not_held_up()
    {
        _batchSize = 2;
        var user = await Merchants.CreateUserAsync(Api, "reconcile-queue@example.com");
        await CreateAsync(user, SilentCard);
        await CreateAsync(user, SilentCard);
        _clock.Advance(TimeSpan.FromSeconds(1));
        var newer = await CreateAsync(user, LateCard);
        _clock.Advance(Grace + TimeSpan.FromSeconds(1));

        // The oldest first: the two silent ones fill the first batch and are put back by the retry interval.
        var first = await ReconcileAsync();
        var second = await ReconcileAsync();

        Assert.Equal(2, first.StillPending);
        Assert.Equal(new ReconciliationRun(Authorized: 1, Failed: 0, StillPending: 0, TakenElsewhere: 0), second);
        Assert.Equal(PaymentStatus.Authorized, await StatusAsync(newer));
    }

    [Fact]
    public async Task Two_reconcilers_at_once_record_the_change_once()
    {
        var user = await Merchants.CreateUserAsync(Api, "reconcile-race@example.com");
        var late = await CreateAsync(user, LateCard);
        _clock.Advance(Grace + TimeSpan.FromSeconds(1));

        var runs = await Task.WhenAll(ReconcileAsync(), ReconcileAsync());

        Assert.Equal(1, runs.Sum(run => run.Authorized));
        Assert.Equal(PaymentStatus.Authorized, await StatusAsync(late));
        Assert.Equal(1, await CountAsync(db => db.PaymentEvents.CountAsync(row => row.PaymentId == late && row.ToStatus == PaymentStatus.Authorized, Cancellation)));
        Assert.Equal(1, await CountAsync(db => db.OutboxMessages.CountAsync(row => row.PaymentId == late, Cancellation)));
    }

    [Fact]
    public async Task The_queue_gauge_counts_the_payments_due()
    {
        var user = await Merchants.CreateUserAsync(Api, "reconcile-gauge@example.com");
        await CreateAsync(user, SilentCard);
        await CreateAsync(user, LateCard);

        await using var scope = Api.Services.CreateAsyncScope();
        var measure = scope.ServiceProvider.GetRequiredService<Application.Webhooks.MeasureQueues>();
        var fresh = await measure.HandleAsync(Cancellation);
        _clock.Advance(Grace + TimeSpan.FromSeconds(1));
        var due = await measure.HandleAsync(Cancellation);

        Assert.Equal(0, fresh.ReconciliationDue);
        Assert.Equal(2, due.ReconciliationDue);
    }

    private async Task<PaymentId> CreateAsync(TestUser user, string card)
    {
        using var client = Clients.For(Api, user);
        using var response = await PaymentRequests.CreateAsync(client, CreatePaymentTests.Body(card));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<CreatedPayment>(Cancellation);
        return new PaymentId(body!.Id);
    }

    private async Task<ReconciliationRun> ReconcileAsync()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ReconcilePendingPayments>().HandleAsync(Cancellation);
    }

    private async Task<PaymentStatus> StatusAsync(PaymentId id) =>
        await CountAsync(db => db.Payments.AsNoTracking().Where(row => row.Id == id).Select(row => row.Status).SingleAsync(Cancellation));

    private async Task<T> CountAsync<T>(Func<PaymentsDbContext, Task<T>> read)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<PaymentsDbContext>());
    }

    private sealed record CreatedPayment(Guid Id);

    /// <summary>The simulator, taking <see cref="AcquirerLatency"/> on the fake clock to authorize.</summary>
    private sealed class SlowAcquirer(IAcquirer inner, FakeTimeProvider clock) : IAcquirer
    {
        public async Task<AcquirerResponse> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken)
        {
            clock.Advance(AcquirerLatency);
            return await inner.AuthorizeAsync(request, cancellationToken);
        }

        public Task<AcquirerResponse> CaptureAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
            inner.CaptureAsync(request, cancellationToken);

        public Task<AcquirerResponse> RefundAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
            inner.RefundAsync(request, cancellationToken);
    }
}
