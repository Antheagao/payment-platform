using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;

namespace PaymentPlatform.ServiceDefaults.Tests;

/// <summary>The sampler <c>AddServiceDefaults</c> installs, read back through a real tracer provider.</summary>
public sealed class TraceSamplingTests : IDisposable
{
    // A source of its own, so spans from other tests in the process never reach this exporter.
    private readonly ActivitySource _source = new($"{ServiceDefaultsExtensions.TelemetryPrefix}.Tests.Sampling.{Guid.NewGuid():N}");
    private readonly List<Activity> _exported = [];
    private readonly IHost _host;

    public TraceSamplingTests()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddInMemoryExporter(_exported));
        _host = builder.Build();
        _host.Services.GetRequiredService<TracerProvider>();
    }

    [Fact]
    public void A_root_span_is_kept_with_its_children()
    {
        using (_source.StartActivity("job hourly"))
        using (_source.StartActivity("SELECT"))
        {
        }

        Assert.Equal(["SELECT", "job hourly"], Names());
    }

    [Fact]
    public void A_root_span_marked_with_priority_zero_is_dropped_with_everything_under_it()
    {
        using (StartQuietRoot())
        using (_source.StartActivity("SELECT"))
        {
        }

        Assert.Empty(Names());
    }

    [Fact]
    public void Work_under_a_dropped_run_that_continues_a_sampled_trace_is_kept_in_that_trace()
    {
        var request = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded, isRemote: true);

        using (StartQuietRoot())
        {
            using (_source.StartActivity("outbox dispatch", ActivityKind.Consumer, request))
            using (_source.StartActivity("INSERT"))
            {
            }

            using (_source.StartActivity("UPDATE"))
            {
            }
        }

        Assert.Equal(["INSERT", "outbox dispatch"], Names());
        var dispatch = Assert.Single(_exported, span => span.DisplayName == "outbox dispatch");
        Assert.Equal((request.TraceId, request.SpanId), (dispatch.TraceId, dispatch.ParentSpanId));
    }

    [Fact]
    public void A_continued_trace_its_caller_did_not_sample_stays_unsampled()
    {
        var request = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.None, isRemote: true);

        using (StartQuietRoot())
        using (_source.StartActivity("outbox dispatch", ActivityKind.Consumer, request))
        {
        }

        Assert.Empty(Names());
    }

    [Fact]
    public void Only_a_priority_of_zero_drops_a_root()
    {
        using (_source.StartActivity("job a", ActivityKind.Internal, parentContext: default, tags: [new(TraceSampling.PriorityTag, 1)]))
        {
        }

        using (_source.StartActivity("job b", ActivityKind.Internal, parentContext: default, tags: [new(TraceSampling.PriorityTag, "0")]))
        {
        }

        Assert.Equal(["job a", "job b"], Names());
    }

    public void Dispose()
    {
        _source.Dispose();
        _host.Dispose();
    }

    private Activity? StartQuietRoot() =>
        _source.StartActivity("job every-second", ActivityKind.Internal, parentContext: default, tags: [new(TraceSampling.PriorityTag, 0)]);

    private List<string> Names() => _exported.Select(span => span.DisplayName).ToList();
}
