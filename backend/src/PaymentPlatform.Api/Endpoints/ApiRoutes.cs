namespace PaymentPlatform.Api.Endpoints;

/// <summary>The route table: health stays outside the version prefix, every merchant endpoint lives under <c>/api/v1</c>.</summary>
public static class ApiRoutes
{
    public const string V1 = "/api/v1";

    /// <summary>
    /// Maps the versioned API. Each resource adds one <c>Map*Endpoints</c> call on this group from its own
    /// file in <c>Endpoints/</c>. Every endpoint requires a valid access token unless it opts out with
    /// <c>AllowAnonymous</c>, as only the login does.
    /// </summary>
    public static RouteGroupBuilder MapApiV1(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        return endpoints.MapGroup(V1).WithTags("v1").RequireAuthorization();
    }
}
