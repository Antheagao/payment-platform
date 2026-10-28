using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace PaymentPlatform.ServiceDefaults.Tests;

public sealed class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task A_well_formed_caller_id_flows_to_the_response_and_the_context()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIds.HeaderName] = "caller-123";
        string? seenByNext = null;

        await Run(context, ctx => seenByNext = ctx.GetCorrelationId());

        Assert.Equal("caller-123", seenByNext);
        Assert.Equal("caller-123", context.Response.Headers[CorrelationIds.HeaderName]);
    }

    [Fact]
    public async Task A_missing_id_is_generated_and_echoed()
    {
        var context = new DefaultHttpContext();

        await Run(context, _ => { });

        var echoed = context.Response.Headers[CorrelationIds.HeaderName].ToString();
        Assert.Matches("^[0-9a-f]{32}$", echoed);
        Assert.Equal(echoed, context.GetCorrelationId());
    }

    [Fact]
    public async Task An_unsafe_caller_id_is_never_echoed()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIds.HeaderName] = "evil\r\nSet-Cookie: x=1";

        await Run(context, _ => { });

        Assert.Matches("^[0-9a-f]{32}$", context.Response.Headers[CorrelationIds.HeaderName].ToString());
    }

    [Fact]
    public async Task The_frameworks_own_request_identifier_is_left_alone()
    {
        var context = new DefaultHttpContext { TraceIdentifier = "0HNPL90QA1J7J:00000001" };

        await Run(context, _ => { });

        Assert.Equal("0HNPL90QA1J7J:00000001", context.TraceIdentifier);
    }

    [Fact]
    public void Outside_the_pipeline_there_is_no_id()
    {
        Assert.Null(new DefaultHttpContext().GetCorrelationId());
    }

    private static Task Run(HttpContext context, Action<HttpContext> next)
    {
        var middleware = new CorrelationIdMiddleware(
            ctx =>
            {
                next(ctx);
                return Task.CompletedTask;
            },
            NullLogger<CorrelationIdMiddleware>.Instance);
        return middleware.InvokeAsync(context);
    }
}
