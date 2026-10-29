using Microsoft.AspNetCore.Http.HttpResults;

namespace PaymentPlatform.Api.Errors;

/// <summary>A problem an endpoint returns on purpose, with its stable <c>code</c> (ADR 006).</summary>
public static class ApiProblems
{
    public static ProblemHttpResult Create(int status, string code, string detail) =>
        TypedResults.Problem(statusCode: status, detail: detail, extensions: new Dictionary<string, object?> { ["code"] = code });
}
