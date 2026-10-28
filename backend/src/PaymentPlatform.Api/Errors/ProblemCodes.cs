namespace PaymentPlatform.Api.Errors;

/// <summary>
/// The stable <c>code</c> for problems the framework produces on its own (ADR 006). Endpoints that know
/// more set a specific code instead, such as <c>payment_not_capturable</c>. Codes are never renamed.
/// </summary>
public static class ProblemCodes
{
    public const string InvalidRequest = "invalid_request";
    public const string Unauthorized = "unauthorized";
    public const string Forbidden = "forbidden";
    public const string NotFound = "not_found";
    public const string MethodNotAllowed = "method_not_allowed";
    public const string Conflict = "conflict";
    public const string PayloadTooLarge = "payload_too_large";
    public const string UnsupportedMediaType = "unsupported_media_type";
    public const string Unprocessable = "unprocessable";
    public const string RateLimited = "rate_limited";
    public const string Internal = "internal_error";
    public const string Unavailable = "service_unavailable";

    public static string ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => InvalidRequest,
        StatusCodes.Status401Unauthorized => Unauthorized,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => NotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status409Conflict => Conflict,
        StatusCodes.Status413PayloadTooLarge => PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status422UnprocessableEntity => Unprocessable,
        StatusCodes.Status429TooManyRequests => RateLimited,
        StatusCodes.Status503ServiceUnavailable => Unavailable,
        >= 500 => Internal,
        _ => InvalidRequest,
    };
}
