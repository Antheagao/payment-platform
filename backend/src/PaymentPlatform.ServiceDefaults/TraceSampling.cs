using OpenTelemetry.Trace;

namespace PaymentPlatform.ServiceDefaults;

/// <summary>
/// Which spans are kept (ADR 005): all of them, as with OpenTelemetry's default parent-based sampler, except a root
/// span started with <see cref="PriorityTag"/> set to <c>0</c> (the OpenTracing convention), which is dropped with
/// everything under it.
/// </summary>
/// <remarks>
/// That is for work that runs too often to be worth a trace per run, the reason health probes are left out: a job
/// polling every second would put 86,400 traces a day in front of the ones people look for. Work such a run does
/// for another trace, such as an outbox event dispatched under the request that wrote it, starts from that trace's
/// context and is kept with it.
/// </remarks>
public static class TraceSampling
{
    public const string PriorityTag = "sampling.priority";

    internal static Sampler Create() => new ParentBasedSampler(new PriorityRootSampler());

    private sealed class PriorityRootSampler : Sampler
    {
        private static readonly SamplingResult Keep = new(SamplingDecision.RecordAndSample);
        private static readonly SamplingResult Skip = new(SamplingDecision.Drop);

        public override SamplingResult ShouldSample(in SamplingParameters samplingParameters)
        {
            foreach (var tag in samplingParameters.Tags ?? [])
            {
                if (tag.Key == PriorityTag && tag.Value is 0)
                {
                    return Skip;
                }
            }

            return Keep;
        }
    }
}
