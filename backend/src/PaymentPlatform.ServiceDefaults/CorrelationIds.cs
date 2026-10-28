using System.Buffers;
using System.Collections;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace PaymentPlatform.ServiceDefaults;

/// <summary>
/// The correlation id: one id per merchant request, carried from the API through the async workflow, so a
/// payment's journey can be followed across both (ADR 005).
/// </summary>
/// <remarks>
/// It is deliberately not ASP.NET Core's <c>RequestId</c>, which the framework already puts in every log
/// scope and which identifies one HTTP request on one connection, not a journey across processes.
/// </remarks>
public static class CorrelationIds
{
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>
    /// The log scope property, PascalCase like the <c>TraceId</c> and <c>SpanId</c> that .NET adds itself; the
    /// API's problem details spell it <c>correlation_id</c> because API JSON is snake_case.
    /// </summary>
    public const string LogScopeKey = "CorrelationId";

    /// <summary>The attribute on the request span, so a trace can be found from the id a merchant reports.</summary>
    public const string SpanTagName = "correlation.id";

    public const int MaxLength = 64;

    private static readonly object ItemKey = new();

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

    /// <summary>The id the middleware assigned to this request, or <see langword="null"/> outside the pipeline.</summary>
    public static string? GetCorrelationId(this HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Items.TryGetValue(ItemKey, out var value) ? value as string : null;
    }

    internal static void SetCorrelationId(this HttpContext context, string correlationId) =>
        context.Items[ItemKey] = correlationId;

    /// <summary>
    /// Opens a log scope carrying the id; the worker calls this with the id stored on an outbox row, so its
    /// lines join the request that caused the work.
    /// </summary>
    public static IDisposable? BeginScope(ILogger logger, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(logger);
        return logger.BeginScope(new CorrelationScope(correlationId));
    }

    // A one-entry scope that also prints well in the scope's "Message" field.
    private sealed class CorrelationScope(string correlationId) : IReadOnlyList<KeyValuePair<string, object?>>
    {
        public int Count => 1;

        public KeyValuePair<string, object?> this[int index] => index == 0
            ? new KeyValuePair<string, object?>(LogScopeKey, correlationId)
            : throw new ArgumentOutOfRangeException(nameof(index));

        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
        {
            yield return this[0];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"{LogScopeKey}:{correlationId}";
    }
}
