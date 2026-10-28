namespace PaymentPlatform.Api;

/// <summary>
/// Headers for a JSON-only API that browsers never call directly (ADR 004): nothing may be framed, sniffed,
/// cached, or used as a script source, and no referrer leaves.
/// </summary>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers.XContentTypeOptions = "nosniff";
        headers.XFrameOptions = "DENY";
        headers.ContentSecurityPolicy = "default-src 'none'; frame-ancestors 'none'";
        headers["Referrer-Policy"] = "no-referrer";
        // Payment data must never sit in a shared cache or the browser's back-forward cache.
        headers.CacheControl = "no-store";
        return next(context);
    }
}
