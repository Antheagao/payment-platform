using System.Buffers;

namespace PaymentPlatform.ServiceDefaults;

/// <summary>
/// The rules for request ids: the correlation id carried from the API through the async workflow (ADR 005).
/// </summary>
public static class RequestIds
{
    public const string HeaderName = "X-Request-Id";

    /// <summary>
    /// The log scope property, PascalCase like the <c>TraceId</c> and <c>SpanId</c> that .NET adds itself; the
    /// API's problem details spell it <c>request_id</c> because API JSON is snake_case.
    /// </summary>
    public const string LogScopeKey = "RequestId";

    /// <summary>The attribute on the request span, so a trace can be found from the id a merchant reports.</summary>
    public const string SpanTagName = "request.id";

    public const int MaxLength = 64;

    // A caller's id is kept for correlation only, never trusted for anything else, so the alphabet is
    // narrow enough that it can never break a log line, a header, or a query.
    private static readonly SearchValues<char> Allowed =
        SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.:");

    /// <summary>Whether a caller-supplied id is safe to keep: 1 to 64 letters, digits, or <c>- _ . :</c>.</summary>
    public static bool IsAcceptable(string? candidate) =>
        !string.IsNullOrEmpty(candidate)
        && candidate.Length <= MaxLength
        && !candidate.AsSpan().ContainsAnyExcept(Allowed);

    /// <summary>A fresh id: 32 lowercase hex characters.</summary>
    public static string New() => Guid.NewGuid().ToString("N");

    /// <summary>Keeps an acceptable caller id, otherwise generates one.</summary>
    public static string AcceptOrCreate(string? candidate) => IsAcceptable(candidate) ? candidate! : New();
}
