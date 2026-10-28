using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace PaymentPlatform.ServiceDefaults.Tests;

public sealed class RequestIdMiddlewareTests
{
    [Fact]
    public async Task A_well_formed_caller_id_flows_to_the_response_and_the_trace_identifier()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestIds.HeaderName] = "caller-123";
        string? seenByNext = null;

        await Run(context, ctx => seenByNext = ctx.TraceIdentifier);

        Assert.Equal("caller-123", seenByNext);
        Assert.Equal("caller-123", context.Response.Headers[RequestIds.HeaderName]);
    }

    [Fact]
    public async Task A_missing_id_is_generated_and_echoed()
    {
        var context = new DefaultHttpContext();

        await Run(context, _ => { });

        var echoed = context.Response.Headers[RequestIds.HeaderName].ToString();
        Assert.Matches("^[0-9a-f]{32}$", echoed);
        Assert.Equal(echoed, context.TraceIdentifier);
    }

    [Fact]
    public async Task An_unsafe_caller_id_is_never_echoed()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[RequestIds.HeaderName] = "evil\r\nSet-Cookie: x=1";

        await Run(context, _ => { });

        Assert.Matches("^[0-9a-f]{32}$", context.Response.Headers[RequestIds.HeaderName].ToString());
    }

    private static Task Run(HttpContext context, Action<HttpContext> next)
    {
        var middleware = new RequestIdMiddleware(
            ctx =>
            {
                next(ctx);
                return Task.CompletedTask;
            },
            NullLogger<RequestIdMiddleware>.Instance);
        return middleware.InvokeAsync(context);
    }
}
