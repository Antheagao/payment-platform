using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaymentPlatform.ServiceDefaults;

/// <summary>
/// Gives every request a request id: keeps an acceptable <c>X-Request-Id</c> from the caller or generates one,
/// echoes it on the response, and puts it in the log scope and on the current span.
/// </summary>
/// <remarks>
/// The id also becomes <see cref="HttpContext.TraceIdentifier"/>, which is what problem details and the
/// audit trail read, so there is one id per request everywhere.
/// </remarks>
public sealed class RequestIdMiddleware(RequestDelegate next, ILogger<RequestIdMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var requestId = RequestIds.AcceptOrCreate(context.Request.Headers[RequestIds.HeaderName]);
        context.TraceIdentifier = requestId;
        context.Response.Headers[RequestIds.HeaderName] = requestId;
        Activity.Current?.SetTag(RequestIds.SpanTagName, requestId);

        using (logger.BeginScope(new Dictionary<string, object?> { [RequestIds.LogScopeKey] = requestId }))
        {
            await next(context);
        }
    }
}
