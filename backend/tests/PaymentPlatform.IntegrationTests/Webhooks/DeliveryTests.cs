using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Time.Testing;
using PaymentPlatform.Application;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Webhooks;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.IntegrationTests.Support;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Webhooks;

/// <summary>
/// The delivery job's rules (ADR 003), against the real database with a scripted receiver and a fake clock:
/// signed requests, retries on the schedule, dead letters, <c>410 Gone</c>, leases, and the trace.
/// </summary>
public sealed class DeliveryTests(SqlServerFixture sqlServer) : ApiTestBase(sqlServer)
{
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
    private readonly ScriptedReceiver _receiver = new();
    private WebApplicationFactory<Program>? _api;

    private WebApplicationFactory<Program> Api => _api ??= Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.AddSingleton<TimeProvider>(_clock);
        services.AddSingleton<IWebhookSender>(_receiver);
    }));

    [Fact]
    public async Task A_delivery_is_signed_sent_once_and_marked_delivered()
    {
        var (user, endpoint) = await MerchantWithEndpointAsync("signed@example.com");
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));

        Assert.Equal(1, await DeliverAsync());

        var request = Assert.Single(_receiver.Requests);
        Assert.Equal(message.EventId.ToString(), request.Id);
        Assert.Equal(message.Payload, request.Body);
        Assert.Equal(new Uri(endpoint.Url), request.Url);
        Assert.Equal(_clock.GetUtcNow().ToUnixTimeSeconds(), request.Timestamp);
        Assert.Equal("seeded-deliveries", request.CorrelationId);
        var key = Convert.FromBase64String(endpoint.Secret["whsec_".Length..]);
        var digest = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{request.Id}.{request.Timestamp}.{request.Body}"));
        Assert.Equal($"v1,{Convert.ToBase64String(digest)}", request.Signature);

        var delivery = await DeliveryForAsync(message);
        Assert.Equal((WebhookDeliveryStatus.Delivered, 1, 200), (delivery.Status, delivery.Attempts, delivery.LastStatusCode));
        Assert.Equal(_clock.GetUtcNow(), delivery.DeliveredAt);
        Assert.Null(delivery.LeasedUntil);
        Assert.Equal(0, await DeliverAsync());
        Assert.Single(_receiver.Requests);
    }

    [Fact]
    public async Task A_failure_is_retried_on_the_schedule_and_dead_lettered_after_the_last_attempt()
    {
        var (user, _) = await MerchantWithEndpointAsync("retried@example.com");
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));
        _receiver.Answer = _ => new WebhookAttempt(500, "HTTP 500");

        Assert.Equal(1, await DeliverAsync());
        var first = await DeliveryForAsync(message);
        Assert.Equal((WebhookDeliveryStatus.Pending, 1, 500, "HTTP 500"), (first.Status, first.Attempts, first.LastStatusCode, first.LastError));
        Assert.InRange(first.NextAttemptAt, _clock.GetUtcNow(), _clock.GetUtcNow().AddSeconds(10));
        Assert.Null(first.LeasedUntil);

        // Not due yet: nothing is sent until the clock reaches the next attempt.
        if (first.NextAttemptAt > _clock.GetUtcNow())
        {
            Assert.Equal(0, await DeliverAsync());
        }

        for (var attempt = 2; attempt <= WebhookRetrySchedule.MaxAttempts; attempt++)
        {
            _clock.Advance(TimeSpan.FromHours(1));
            Assert.Equal(1, await DeliverAsync());
        }

        var last = await DeliveryForAsync(message);
        Assert.Equal((WebhookDeliveryStatus.DeadLettered, WebhookRetrySchedule.MaxAttempts), (last.Status, last.Attempts));
        Assert.Equal(WebhookRetrySchedule.MaxAttempts, _receiver.Requests.Count);
        _clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(0, await DeliverAsync());
    }

    [Fact]
    public async Task A_delivery_that_gets_no_answer_is_retried_with_the_reason()
    {
        var (user, _) = await MerchantWithEndpointAsync("silent@example.com");
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));
        _receiver.Answer = _ => new WebhookAttempt(null, "no answer within 10 s");

        await DeliverAsync();

        var delivery = await DeliveryForAsync(message);
        Assert.Equal((WebhookDeliveryStatus.Pending, 1, null, "no answer within 10 s"), (delivery.Status, delivery.Attempts, delivery.LastStatusCode, delivery.LastError));
    }

    [Fact]
    public async Task A_410_dead_letters_the_delivery_and_disables_the_endpoint()
    {
        var (user, _) = await MerchantWithEndpointAsync("gone@example.com");
        var messages = await SeedDeliveriesAsync(user, count: 1);
        _receiver.Answer = _ => new WebhookAttempt(410, "HTTP 410");

        await DeliverAsync();

        var delivery = await DeliveryForAsync(messages[0]);
        Assert.Equal((WebhookDeliveryStatus.DeadLettered, 410), (delivery.Status, delivery.LastStatusCode));
        Assert.False((await EndpointForAsync(user)).Enabled);
    }

    [Fact]
    public async Task A_disabled_endpoints_deliveries_are_dead_lettered_without_being_sent()
    {
        var (user, _) = await MerchantWithEndpointAsync("paused@example.com");
        var messages = await SeedDeliveriesAsync(user, count: 2);
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            (await db.WebhookEndpoints.SingleAsync(endpoint => endpoint.MerchantId == user.MerchantId, Cancellation)).Disable(_clock.GetUtcNow());
            await db.SaveChangesAsync(Cancellation);
        }

        Assert.Equal(2, await DeliverAsync());

        Assert.Empty(_receiver.Requests);
        foreach (var message in messages)
        {
            var delivery = await DeliveryForAsync(message);
            Assert.Equal((WebhookDeliveryStatus.DeadLettered, "the endpoint is disabled"), (delivery.Status, delivery.LastError));
        }
    }

    [Fact]
    public async Task A_delivery_leased_by_another_worker_waits_until_the_lease_runs_out()
    {
        var (user, _) = await MerchantWithEndpointAsync("leased@example.com");
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));
        await SetLeaseAsync(message, _clock.GetUtcNow().AddSeconds(30));

        Assert.Equal(0, await DeliverAsync());
        Assert.Empty(_receiver.Requests);

        // The other worker died: its lease runs out and the delivery is taken again.
        _clock.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(1, await DeliverAsync());
        Assert.Equal(WebhookDeliveryStatus.Delivered, (await DeliveryForAsync(message)).Status);
    }

    [Fact]
    public async Task A_worker_whose_lease_ran_out_mid_send_does_not_overwrite_the_worker_that_took_over()
    {
        var (user, _) = await MerchantWithEndpointAsync("overtaken@example.com");
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));
        var takenOver = _clock.GetUtcNow().AddMinutes(5);

        // While this worker waits on the receiver, its lease runs out and another worker leases the delivery.
        _receiver.Answer = _ =>
        {
            SetLeaseAsync(message, takenOver).GetAwaiter().GetResult();
            return new WebhookAttempt(500, "HTTP 500");
        };
        await DeliverAsync();

        var delivery = await DeliveryForAsync(message);
        Assert.Equal((WebhookDeliveryStatus.Pending, 0, takenOver), (delivery.Status, delivery.Attempts, delivery.LeasedUntil));
        Assert.Null(delivery.LastError);
    }

    [Fact]
    public async Task A_save_that_races_a_takeover_is_refused_by_the_lease_token()
    {
        var (user, _) = await MerchantWithEndpointAsync("token@example.com");
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));
        var takenOver = _clock.GetUtcNow().AddMinutes(5);

        // The takeover lands after this worker read the delivery back and before its save reaches the database.
        var interceptor = new TakeoverBeforeSave(() => SetLeaseAsync(message, takenOver));
        await using var raced = Api.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.ConfigureDbContext<PaymentsDbContext>(options => options.AddInterceptors(interceptor))));
        await using (var scope = raced.Services.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<DeliverWebhooks>().HandleAsync(Cancellation);
        }

        Assert.True(interceptor.Fired);
        var delivery = await DeliveryForAsync(message);
        Assert.Equal((WebhookDeliveryStatus.Pending, 0, takenOver), (delivery.Status, delivery.Attempts, delivery.LeasedUntil));
    }

    [Fact]
    public async Task A_worker_skips_deliveries_another_holds_instead_of_waiting_for_them()
    {
        var (user, _) = await MerchantWithEndpointAsync("skip-held@example.com");
        var messages = await SeedDeliveriesAsync(user, count: 30);

        // Another worker mid-claim: it holds the update locks on twenty of the due deliveries.
        await using var holderScope = Api.Services.CreateAsyncScope();
        await using var holder = new SqlConnection(holderScope.ServiceProvider.GetRequiredService<PaymentsDbContext>().Database.GetConnectionString());
        await holder.OpenAsync(Cancellation);
        await using var held = (SqlTransaction)await holder.BeginTransactionAsync(Cancellation);
        await using (var claim = new SqlCommand(
            "SELECT TOP (20) Id FROM payments.WebhookDeliveries WITH (UPDLOCK, ROWLOCK) WHERE MerchantId = @merchant ORDER BY Id", holder, held))
        {
            claim.Parameters.AddWithValue("@merchant", user.MerchantId.Value);
            await using var reader = await claim.ExecuteReaderAsync(Cancellation);
            while (await reader.ReadAsync(Cancellation))
            {
            }
        }

        // READPAST lets this worker send the other ten at once; without it, it would queue behind the locks.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            Assert.Equal(10, await scope.ServiceProvider.GetRequiredService<DeliverWebhooks>().HandleAsync(timeout.Token));
        }

        await held.RollbackAsync(Cancellation);
        Assert.Equal(20, await DeliverAllAsync());
        Assert.Equal(messages.Count, _receiver.Requests.Select(request => request.Id).Distinct().Count());
    }

    [Fact]
    public async Task Workers_running_at_once_send_each_delivery_once()
    {
        var (user, _) = await MerchantWithEndpointAsync("shared@example.com");
        var messages = await SeedDeliveriesAsync(user, count: 120);

        var taken = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(DeliverAllAsync, Cancellation)));

        Assert.Equal(120, taken.Sum());
        Assert.Equal(messages.Select(message => message.EventId.ToString()).Order(), _receiver.Requests.Select(request => request.Id).Order());
        Assert.True(taken.Count(count => count > 0) > 1, "the work was shared, not done by one worker");
    }

    [Fact]
    public async Task Each_delivery_is_a_producer_span_in_the_trace_of_the_request_that_caused_it()
    {
        var (user, _) = await MerchantWithEndpointAsync("traced-delivery@example.com");
        // The request that made the change: the outbox stores its id as the traceparent.
        var request = new Activity("POST /api/v1/payments").SetIdFormat(ActivityIdFormat.W3C);
        request.ActivityTraceFlags = ActivityTraceFlags.Recorded;
        request.Start();
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));
        request.Stop();
        var spans = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == ApplicationTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = spans.Add,
        };
        ActivitySource.AddActivityListener(listener);

        await DeliverAsync();

        var span = Assert.Single(spans, candidate => candidate.OperationName == "webhook deliver" && Equals(candidate.GetTagItem("messaging.message.id"), message.EventId));
        Assert.Equal(ActivityKind.Producer, span.Kind);
        Assert.Equal(request.TraceId, span.TraceId);
        Assert.Equal(request.SpanId, span.ParentSpanId);
        Assert.Equal(1, span.GetTagItem("webhook.attempt"));
        Assert.Equal(200, span.GetTagItem("http.response.status_code"));
    }

    [Fact]
    public async Task A_rotated_secret_signs_every_delivery_after_the_rotation()
    {
        var (user, _) = await MerchantWithEndpointAsync("rotated@example.com");
        var message = Assert.Single(await SeedDeliveriesAsync(user, count: 1));
        string rotated;
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RotateWebhookSecret>().HandleAsync(user.MerchantId, Cancellation);
            rotated = result.Value.Secret!;
        }

        await DeliverAsync();

        var request = Assert.Single(_receiver.Requests);
        Assert.Equal(message.EventId.ToString(), request.Id);
        Assert.Equal(WebhookSignature.Sign(WebhookSecrets.KeyOf(rotated)!, request.Id, request.Timestamp, request.Body), request.Signature);
    }

    [Fact]
    public async Task Each_attempt_is_counted_by_how_it_ended()
    {
        var (user, _) = await MerchantWithEndpointAsync("outcomes@example.com");
        var messages = await SeedDeliveriesAsync(user, count: 3);
        var answers = new Dictionary<string, WebhookAttempt>
        {
            [messages[0].EventId.ToString()] = new(200, null),
            [messages[1].EventId.ToString()] = new(500, "HTTP 500"),
            [messages[2].EventId.ToString()] = new(410, "HTTP 410"),
        };
        _receiver.Answer = request => answers[request.Id];
        using var attempts = new MetricCollector<long>(Api.Services.GetRequiredService<IMeterFactory>(), QueueMetrics.MeterName, "webhook.delivery.attempts");

        await DeliverAsync();

        Assert.Equal(
            ["dead_lettered:1", "delivered:1", "retried:1"],
            attempts.GetMeasurementSnapshot().Select(measurement => $"{measurement.Tags[QueueMetrics.OutcomeTag]}:{measurement.Value}").Order());
    }

    [Fact]
    public async Task The_queue_gauges_report_the_last_measurement_and_match_the_tables()
    {
        // The gauges exist (their singleton is made) but have nothing to report until the first measurement.
        var meters = Api.Services.GetRequiredService<IMeterFactory>();
        Assert.Null(Api.Services.GetRequiredService<QueueMetrics>().Last);
        using var due = new MetricCollector<long>(meters, QueueMetrics.MeterName, "webhook.deliveries.due");
        Assert.NotNull(due.Instrument);
        due.RecordObservableInstruments();
        Assert.Empty(due.GetMeasurementSnapshot());

        // Deliveries in each state, an event the dispatcher has not reached, and a captured payment past its delay.
        var (user, _) = await MerchantWithEndpointAsync("gauges@example.com");
        var messages = await SeedDeliveriesAsync(user, count: 4);
        _receiver.Answer = request =>
            request.Id == messages[0].EventId.ToString() ? new WebhookAttempt(500, "HTTP 500")
            : request.Id == messages[1].EventId.ToString() ? new WebhookAttempt(410, "HTTP 410")
            : new WebhookAttempt(200, null);
        await SetNextAttemptAsync(messages[2], _clock.GetUtcNow().AddMinutes(5));
        await DeliverAsync();
        await SeedUndispatchedAsync(user);
        _clock.Advance(TimeSpan.FromSeconds(90));

        QueueSnapshot snapshot;
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            snapshot = await scope.ServiceProvider.GetRequiredService<MeasureQueues>().HandleAsync(Cancellation);
        }

        var now = _clock.GetUtcNow();
        var expected = await ExpectedSnapshotAsync(now);
        Assert.Equal(expected, snapshot);
        Assert.True(snapshot.OutboxPending >= 1 && snapshot.DeliveriesRetrying >= 1 && snapshot.DeliveriesDeadLettered >= 1, snapshot.ToString());
        Assert.True(snapshot.OutboxOldestAge >= TimeSpan.FromSeconds(90), snapshot.ToString());

        due.RecordObservableInstruments();
        Assert.Equal(snapshot.DeliveriesDue, due.LastMeasurement!.Value);
        using var lag = new MetricCollector<double>(meters, QueueMetrics.MeterName, "webhook.deliveries.lag");
        lag.RecordObservableInstruments();
        Assert.Equal(snapshot.DeliveryLag.TotalSeconds, lag.LastMeasurement!.Value);
        using var deadLettered = new MetricCollector<long>(meters, QueueMetrics.MeterName, "webhook.deliveries.dead_lettered");
        deadLettered.RecordObservableInstruments();
        Assert.Equal(snapshot.DeliveriesDeadLettered, deadLettered.LastMeasurement!.Value);
    }

    /// <summary>The snapshot written as plain SQL, independently of <see cref="MeasureQueues"/>.</summary>
    private async Task<QueueSnapshot> ExpectedSnapshotAsync(DateTimeOffset now)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        async Task<long> Count(FormattableString sql) => (await db.Database.SqlQuery<long>(sql).ToListAsync(Cancellation)).Single();
        async Task<TimeSpan> Age(FormattableString sql) =>
            (await db.Database.SqlQuery<DateTimeOffset?>(sql).ToListAsync(Cancellation)).Single() is { } at && now > at ? now - at : TimeSpan.Zero;
        var capturedBefore = now - TimeSpan.FromDays(1);
        return new QueueSnapshot(
            await Count($"SELECT COUNT_BIG(*) AS [Value] FROM payments.OutboxMessages WHERE DispatchedAt IS NULL"),
            await Age($"SELECT MIN(OccurredAt) AS [Value] FROM payments.OutboxMessages WHERE DispatchedAt IS NULL"),
            await Count($"SELECT COUNT_BIG(*) AS [Value] FROM payments.WebhookDeliveries WHERE Status = 'Pending' AND NextAttemptAt <= {now}"),
            await Age($"SELECT MIN(NextAttemptAt) AS [Value] FROM payments.WebhookDeliveries WHERE Status = 'Pending' AND NextAttemptAt <= {now}"),
            await Count($"SELECT COUNT_BIG(*) AS [Value] FROM payments.WebhookDeliveries WHERE Status = 'Pending' AND Attempts > 0"),
            await Count($"SELECT COUNT_BIG(*) AS [Value] FROM payments.WebhookDeliveries WHERE Status = 'DeadLettered'"),
            await Count($"SELECT COUNT_BIG(*) AS [Value] FROM payments.Payments WHERE Status = 'Captured' AND UpdatedAt < {capturedBefore}"));
    }

    private async Task SeedUndispatchedAsync(TestUser user)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var vault = scope.ServiceProvider.GetRequiredService<ICardVault>();
        var now = _clock.GetUtcNow();
        var card = vault.Tokenize(new CardInput("4242424242424242", 12, now.Year + 2, "123")).Value;
        var payment = Payment.Create(PaymentId.New(now), user.MerchantId, Money.Create(100, "USD").Value, card, null, Actor.MerchantUser(user.UserId.Value), now);
        payment.Authorize("sim_auth_undispatched", Actor.MerchantUser(user.UserId.Value), now);
        db.Payments.Add(payment);
        db.RecordChanges(payment, "seeded-undispatched");
        await db.SaveChangesAsync(Cancellation);
    }

    private async Task SetNextAttemptAsync(OutboxMessage message, DateTimeOffset nextAttemptAt)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().WebhookDeliveries
            .Where(delivery => delivery.OutboxMessageId == message.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(delivery => delivery.NextAttemptAt, nextAttemptAt), Cancellation);
    }

    private async Task<int> DeliverAsync()
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<DeliverWebhooks>().HandleAsync(Cancellation);
    }

    private async Task<int> DeliverAllAsync()
    {
        var total = 0;
        int taken;
        do
        {
            taken = await DeliverAsync();
            total += taken;
        }
        while (taken > 0);

        return total;
    }

    private async Task<(TestUser User, WebhookEndpoint Endpoint)> MerchantWithEndpointAsync(string email)
    {
        var user = await Merchants.CreateUserAsync(Api, email, merchantName: email);
        await using var scope = Api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var endpoint = WebhookEndpoint.Create(Guid.NewGuid(), user.MerchantId, $"https://{FakeHostResolver.PublicHost}/{user.MerchantId.Value}", WebhookSecrets.New(), _clock.GetUtcNow());
        db.WebhookEndpoints.Add(endpoint);
        await db.SaveChangesAsync(Cancellation);
        return (user, endpoint);
    }

    /// <summary>Authorized payments with their outbox events dispatched into one pending delivery each.</summary>
    private async Task<List<OutboxMessage>> SeedDeliveriesAsync(TestUser user, int count)
    {
        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
            var vault = scope.ServiceProvider.GetRequiredService<ICardVault>();
            var now = _clock.GetUtcNow();
            for (var index = 0; index < count; index++)
            {
                var card = vault.Tokenize(new CardInput("4242424242424242", 12, now.Year + 2, "123")).Value;
                var payment = Payment.Create(
                    PaymentId.New(now), user.MerchantId, Money.Create(100 + index, "USD").Value, card, null, Actor.MerchantUser(user.UserId.Value), now);
                payment.Authorize($"sim_auth_{index}", Actor.MerchantUser(user.UserId.Value), now);
                db.Payments.Add(payment);
                db.RecordChanges(payment, "seeded-deliveries");
            }

            await db.SaveChangesAsync(Cancellation);
        }

        await using (var scope = Api.Services.CreateAsyncScope())
        {
            var dispatch = scope.ServiceProvider.GetRequiredService<DispatchOutboxMessages>();
            while (await dispatch.HandleAsync(Cancellation) > 0)
            {
            }
        }

        await using (var scope = Api.Services.CreateAsyncScope())
        {
            return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().OutboxMessages.AsNoTracking()
                .Where(message => message.MerchantId == user.MerchantId).OrderBy(message => message.Id).ToListAsync(Cancellation);
        }
    }

    private async Task<WebhookDelivery> DeliveryForAsync(OutboxMessage message)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().WebhookDeliveries.AsNoTracking()
            .SingleAsync(delivery => delivery.OutboxMessageId == message.Id, Cancellation);
    }

    private async Task<WebhookEndpoint> EndpointForAsync(TestUser user)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().WebhookEndpoints.AsNoTracking()
            .SingleAsync(endpoint => endpoint.MerchantId == user.MerchantId, Cancellation);
    }

    private async Task SetLeaseAsync(OutboxMessage message, DateTimeOffset leasedUntil)
    {
        await using var scope = Api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<PaymentsDbContext>().WebhookDeliveries
            .Where(delivery => delivery.OutboxMessageId == message.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(delivery => delivery.LeasedUntil, leasedUntil), Cancellation);
    }

    /// <summary>Runs <paramref name="takeover"/> once, just before the first save that records a delivery attempt.</summary>
    private sealed class TakeoverBeforeSave(Func<Task> takeover) : SaveChangesInterceptor
    {
        private int _fired;

        public bool Fired => _fired == 1;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(eventData);
            if (eventData.Context!.ChangeTracker.Entries<WebhookDelivery>().Any(entry => entry.State == EntityState.Modified)
                && Interlocked.Exchange(ref _fired, 1) == 0)
            {
                await takeover();
            }

            return result;
        }
    }

    /// <summary>Answers for merchants' servers: 200 unless the test says otherwise, and remembers every request.</summary>
    private sealed class ScriptedReceiver : IWebhookSender
    {
        private readonly ConcurrentQueue<WebhookRequest> _requests = new();

        public Func<WebhookRequest, WebhookAttempt> Answer { get; set; } = _ => new WebhookAttempt(200, null);

        public IReadOnlyList<WebhookRequest> Requests => [.. _requests];

        public Task<WebhookAttempt> SendAsync(WebhookRequest request, CancellationToken cancellationToken)
        {
            _requests.Enqueue(request);
            return Task.FromResult(Answer(request));
        }
    }
}
