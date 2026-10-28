using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
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

    private CountingJob NewJob(bool failFirstRun = false) =>
        new(_services.GetRequiredService<IServiceScopeFactory>(), _clock, new WorkerTelemetry(MeterFactory), _logger, failFirstRun);

    private sealed class CountingJob(
        IServiceScopeFactory scopes, TimeProvider clock, WorkerTelemetry telemetry, ILogger logger, bool failFirstRun)
        : PeriodicJob(scopes, clock, telemetry, logger)
    {
        private readonly Channel<int> _completed = Channel.CreateUnbounded<int>();

        public int Runs { get; private set; }

        public List<IServiceProvider> ScopesSeen { get; } = [];

        public override string Name => "test-job";

        public override TimeSpan Interval => PeriodicJobTests.Interval;

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
