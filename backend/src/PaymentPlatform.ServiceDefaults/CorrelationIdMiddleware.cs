using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaymentPlatform.ServiceDefaults;

/// <summary>
/// Gives every request a correlation id: keeps an acceptable <c>X-Correlation-Id</c> from the caller or
/// generates one, echoes it on the response, and puts it in the log scope and on the current span.
/// </summary>
/// <remarks>Use cases read it with <see cref="CorrelationIds.GetCorrelationId"/> to stamp audit and outbox rows.</remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var correlationId = CorrelationIds.AcceptOrCreate(context.Request.Headers[CorrelationIds.HeaderName]);
        context.SetCorrelationId(correlationId);
        context.Response.Headers[CorrelationIds.HeaderName] = correlationId;
        Activity.Current?.SetTag(CorrelationIds.SpanTagName, correlationId);

        using (CorrelationIds.BeginScope(logger, correlationId))
        {
            await next(context);
        }
    }
}
