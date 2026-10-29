using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using PaymentPlatform.ServiceDefaults;
using PaymentPlatform.Worker.Jobs;

namespace PaymentPlatform.Worker.Tests.Jobs;

public sealed class PeriodicJobTests : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));
    private readonly ServiceProvider _services = new ServiceCollection().AddMetrics().BuildServiceProvider();
    private readonly FakeLogger _logger = new();
    private readonly CancellationToken _cancellation = TestContext.Current.CancellationToken;

    [Fact]
    public async Task It_runs_at_startup_and_then_once_per_interval()
    {
        var job = NewJob();

        await job.StartAsync(_cancellation);
        await job.NextRunAsync(_cancellation);
        Assert.Equal(1, job.Runs);

        _clock.Advance(Interval);
        await job.NextRunAsync(_cancellation);
        Assert.Equal(2, job.Runs);

        await job.StopAsync(_cancellation);
    }

    [Fact]
    public async Task A_failed_run_is_logged_and_counted_and_the_next_run_still_happens()
    {
        var job = NewJob(failFirstRun: true);
        using var failures = new MetricCollector<long>(MeterFactory, WorkerTelemetry.Name, "worker.job.failures");

        await job.StartAsync(_cancellation);
        await job.NextRunAsync(_cancellation);
        _clock.Advance(Interval);
        await job.NextRunAsync(_cancellation);
        await job.StopAsync(_cancellation);

        Assert.Equal(2, job.Runs);
        var failure = Assert.Single(failures.GetMeasurementSnapshot());
        Assert.Equal(1, failure.Value);
        Assert.Equal("test-job", failure.Tags["job.name"]);
        var logged = Assert.Single(_logger.Collector.GetSnapshot(), record => record.Level == LogLevel.Error);
        Assert.IsType<InvalidOperationException>(logged.Exception);
    }

    [Fact]
    public async Task Each_run_gets_its_own_scope()
    {
        var job = NewJob();

        await job.RunGuardedAsync(_cancellation);
        await job.RunGuardedAsync(_cancellation);

        Assert.Equal(2, job.ScopesSeen.Count);
        Assert.NotSame(job.ScopesSeen[0], job.ScopesSeen[1]);
    }

    [Fact]
    public async Task Every_run_records_its_duration_on_the_injected_clock()
    {
        var job = NewJob();
        using var durations = new MetricCollector<double>(MeterFactory, WorkerTelemetry.Name, "worker.job.duration");

        await job.RunGuardedAsync(_cancellation);

        var sample = Assert.Single(durations.GetMeasurementSnapshot());
        Assert.Equal(0, sample.Value);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, 0)]
    public async Task A_job_that_does_not_trace_its_runs_asks_the_sampler_to_drop_them(bool traceRuns, int? priority)
    {
        var job = NewJob(traceRuns: traceRuns);
        var seen = new List<object?>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkerTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                if (options.Name == "job test-job")
                {
                    seen.Add(options.Tags?.FirstOrDefault(tag => tag.Key == TraceSampling.PriorityTag).Value);
                }

                return ActivitySamplingResult.AllDataAndRecorded;
            },
        };
        ActivitySource.AddActivityListener(listener);

        await job.RunGuardedAsync(_cancellation);

        Assert.Equal(1, job.Runs);
        Assert.Equal(priority, (int?)Assert.Single(seen));
    }

    [Fact]
    public async Task The_outbox_dispatcher_runs_every_second_untraced_and_the_hourly_cleanup_traced()
    {
        var scopes = _services.GetRequiredService<IServiceScopeFactory>();
        var telemetry = new WorkerTelemetry(MeterFactory);
        PeriodicJob[] jobs =
        [
            new DispatchOutboxJob(scopes, _clock, telemetry, NullLogger<DispatchOutboxJob>.Instance),
            new DeleteExpiredIdempotencyKeysJob(scopes, _clock, telemetry, NullLogger<DeleteExpiredIdempotencyKeysJob>.Instance),
        ];
        var priorities = new Dictionary<string, object?>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == WorkerTelemetry.Name,
            Sample = (ref ActivityCreationOptions<ActivityContext> options) =>
            {
                if (options.Name is "job dispatch-outbox" or "job delete-expired-idempotency-keys")
                {
                    priorities[options.Name] = options.Tags?.FirstOrDefault(tag => tag.Key == TraceSampling.PriorityTag).Value;
                }

                return ActivitySamplingResult.AllDataAndRecorded;
            },
        };
        ActivitySource.AddActivityListener(listener);

        // No use cases are registered here, so each run fails after its span starts; the span is what is checked.
        foreach (var job in jobs)
        {
            await job.RunGuardedAsync(_cancellation);
        }

        Assert.Equal(TimeSpan.FromSeconds(1), jobs[0].Interval);
        Assert.Equal(0, priorities["job dispatch-outbox"]);
        Assert.Null(priorities["job delete-expired-idempotency-keys"]);
    }

    [Fact]
    public async Task Stopping_ends_the_loop_without_a_failure()
    {
        var job = NewJob();
        using var failures = new MetricCollector<long>(MeterFactory, WorkerTelemetry.Name, "worker.job.failures");

        await job.StartAsync(_cancellation);
        await job.NextRunAsync(_cancellation);
        await job.StopAsync(_cancellation);

        Assert.NotNull(job.ExecuteTask);
        Assert.True(job.ExecuteTask.IsCompletedSuccessfully);
        Assert.Empty(failures.GetMeasurementSnapshot());
    }

    public void Dispose() => _services.Dispose();

    private IMeterFactory MeterFactory => _services.GetRequiredService<IMeterFactory>();

    private CountingJob NewJob(bool failFirstRun = false, bool traceRuns = true) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _clock, new WorkerTelemetry(MeterFactory), _logger, failFirstRun, traceRuns);

    private sealed class CountingJob(
        IServiceScopeFactory scopes, TimeProvider clock, WorkerTelemetry telemetry, ILogger logger, bool failFirstRun, bool traceRuns)
        : PeriodicJob(scopes, clock, telemetry, logger)
    {
        private readonly Channel<int> _completed = Channel.CreateUnbounded<int>();

        public int Runs { get; private set; }

        public List<IServiceProvider> ScopesSeen { get; } = [];

        public override string Name => "test-job";

        public override TimeSpan Interval => PeriodicJobTests.Interval;

        protected override bool TraceRuns => traceRuns;

        public async Task NextRunAsync(CancellationToken cancellationToken) =>
            await _completed.Reader.ReadAsync(cancellationToken);

        protected override Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            ScopesSeen.Add(services);
            Runs++;
            _completed.Writer.TryWrite(Runs);
            return failFirstRun && Runs == 1
                ? Task.FromException(new InvalidOperationException("the first run fails"))
                : Task.CompletedTask;
        }
    }
}
