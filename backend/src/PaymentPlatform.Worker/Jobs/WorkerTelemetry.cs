using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// The worker's spans and metrics. Every job run is one span and one duration sample; a failed run is also
/// counted, so a job that keeps failing shows on a dashboard before anyone reads a log.
/// </summary>
public sealed class WorkerTelemetry
{
    public const string Name = "PaymentPlatform.Worker";

    public static readonly ActivitySource ActivitySource = new(Name);

    private readonly Histogram<double> _runDuration;
    private readonly Counter<long> _runFailures;

    public WorkerTelemetry(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        var meter = meterFactory.Create(Name);
        _runDuration = meter.CreateHistogram<double>(
            "worker.job.duration", unit: "s", description: "How long one run of a job took.");
        _runFailures = meter.CreateCounter<long>(
            "worker.job.failures", unit: "{run}", description: "Runs of a job that ended in an exception.");
    }

    public void RecordRun(string job, TimeSpan duration, bool failed)
    {
        var tag = new KeyValuePair<string, object?>("job.name", job);
        _runDuration.Record(duration.TotalSeconds, tag);
        if (failed)
        {
            _runFailures.Add(1, tag);
        }
    }
}
