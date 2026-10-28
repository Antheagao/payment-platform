using System.Diagnostics;
using PaymentPlatform.ServiceDefaults;

namespace PaymentPlatform.Api.Errors;

/// <summary>
/// Makes every error response one shape: RFC 9457 problem details with <c>code</c>, <c>correlation_id</c>, and
/// <c>trace_id</c> (ADR 006), whether an endpoint, the exception handler, or status code pages produced it.
/// </summary>
public static class ProblemDetailsSetup
{
    public static IServiceCollection AddApiProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = Customize);

    private static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;
        var status = problem.Status ?? context.HttpContext.Response.StatusCode;

        // The framework adds a camelCase traceId; the API's JSON is snake_case, so it is replaced below.
        problem.Extensions.Remove("traceId");
        problem.Extensions.TryAdd("code", ProblemCodes.ForStatus(status));
        problem.Extensions["correlation_id"] = context.HttpContext.GetCorrelationId();
        problem.Extensions["trace_id"] = Activity.Current?.TraceId.ToString();

        // An unhandled exception's message can carry SQL or internal names; it is logged, never returned.
        if (context.Exception is not null)
        {
            problem.Detail = "an unexpected error occurred; quote the correlation_id when reporting it";
        }

        // Status code pages answer unknown routes and wrong methods with no detail; say which in plain words.
        problem.Detail ??= status switch
        {
            StatusCodes.Status404NotFound => "no resource matches this path",
            StatusCodes.Status405MethodNotAllowed => "this path does not accept that method",
            _ => null,
        };
    }
}
