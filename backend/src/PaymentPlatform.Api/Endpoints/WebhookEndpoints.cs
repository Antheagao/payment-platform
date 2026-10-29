using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.HttpResults;
using PaymentPlatform.Api.Auth;
using PaymentPlatform.Api.Errors;
using PaymentPlatform.Application.Webhooks;

namespace PaymentPlatform.Api.Endpoints;

/// <summary>
/// <c>GET</c> and <c>PUT /api/v1/webhook-endpoint</c> and <c>POST /api/v1/webhook-endpoint/rotate-secret</c>: the
/// caller's merchant's one webhook endpoint (assumption 11), a singleton resource, so no id in the path.
/// </summary>
public static class WebhookEndpoints
{
    public const string Path = "/webhook-endpoint";

    public static RouteGroupBuilder MapWebhookEndpoints(this RouteGroupBuilder group)
    {
        ArgumentNullException.ThrowIfNull(group);

        group.MapGet(Path, GetAsync)
            .WithName("GetWebhookEndpoint")
            .Produces<WebhookEndpointResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut(Path, SetAsync)
            .WithName("SetWebhookEndpoint")
            .Produces<WebhookEndpointResponse>()
            .Produces<WebhookEndpointResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized);
        group.MapPost($"{Path}/rotate-secret", RotateSecretAsync)
            .WithName("RotateWebhookSecret")
            .Produces<WebhookEndpointResponse>()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return group;
    }

    /// <param name="Enabled">Defaults to true; false stops new deliveries without forgetting the URL or the secret.</param>
    public sealed record SetWebhookEndpointRequest(string? Url, bool? Enabled);

    /// <param name="Secret">Only on the answer that issues it: the first <c>PUT</c> and each rotation.</param>
    public sealed record WebhookEndpointResponse(
        string Url,
        bool Enabled,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Secret)
    {
        public static WebhookEndpointResponse From(WebhookEndpointDetails endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            return new WebhookEndpointResponse(endpoint.Url, endpoint.Enabled, endpoint.CreatedAt, endpoint.UpdatedAt, endpoint.Secret);
        }
    }

    private static async Task<Results<Ok<WebhookEndpointResponse>, ProblemHttpResult>> GetAsync(
        ClaimsPrincipal caller, GetWebhookEndpoint getEndpoint, CancellationToken cancellationToken)
    {
        var endpoint = await getEndpoint.HandleAsync(caller.GetMerchantId(), cancellationToken);
        return endpoint is null
            ? NotFound()
            : TypedResults.Ok(WebhookEndpointResponse.From(endpoint));
    }

    /// <summary>
    /// Creates the endpoint (<c>201</c>, with its secret) or updates it (<c>200</c>, without); repeating the same
    /// request changes nothing further, so it needs no <c>Idempotency-Key</c>.
    /// </summary>
    private static async Task<Results<Created<WebhookEndpointResponse>, Ok<WebhookEndpointResponse>, ValidationProblem>> SetAsync(
        SetWebhookEndpointRequest request, ClaimsPrincipal caller, SetWebhookEndpoint setEndpoint, CancellationToken cancellationToken)
    {
        var (result, urlError) = await setEndpoint.HandleAsync(caller.GetMerchantId(), request.Url, request.Enabled ?? true, cancellationToken);
        if (result is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["url"] = [urlError!] });
        }

        var body = WebhookEndpointResponse.From(result.Endpoint);
        return result.Created
            ? TypedResults.Created($"{ApiRoutes.V1}{Path}", body)
            : TypedResults.Ok(body);
    }

    private static async Task<Results<Ok<WebhookEndpointResponse>, ProblemHttpResult>> RotateSecretAsync(
        ClaimsPrincipal caller, RotateWebhookSecret rotate, CancellationToken cancellationToken)
    {
        var result = await rotate.HandleAsync(caller.GetMerchantId(), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(WebhookEndpointResponse.From(result.Value))
            : NotFound();
    }

    private static ProblemHttpResult NotFound() => ApiProblems.Create(
        StatusCodes.Status404NotFound, WebhookEndpointErrors.NotFound.Code, WebhookEndpointErrors.NotFound.Message);
}
