using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace PaymentPlatform.Application.Acquiring;

/// <summary>
/// A stand-in for the card network: deterministic and stateless. The outcome depends only on the card token
/// (see <see cref="SimulatedCardTokens"/>) and the reference only on the payment id, so the API and the worker
/// agree without shared state, and a repeat call returns the original answer as the port requires.
/// </summary>
public sealed class SimulatedAcquirer(IOptions<SimulatedAcquirerOptions> options, TimeProvider timeProvider) : IAcquirer
{
    public const string AuthorizationPrefix = "sim_auth_";

    private readonly TimeSpan _latency = TimeSpan.FromMilliseconds(options.Value.LatencyMilliseconds);

    public async Task<AcquirerResponse> AuthorizeAsync(AuthorizationRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = Start("authorize", request.PaymentId.Value);
        await WaitForNetworkAsync(cancellationToken);

        var response = request.CardToken switch
        {
            SimulatedCardTokens.CardDeclined => AcquirerResponse.Declined("card_declined"),
            SimulatedCardTokens.InsufficientFunds => AcquirerResponse.Declined("insufficient_funds"),
            SimulatedCardTokens.AcquirerUnavailable => AcquirerResponse.Unavailable(),
            _ => AcquirerResponse.Approved(AuthorizationPrefix + request.PaymentId.Value.ToString("N")),
        };
        return Finish(activity, response);
    }

    public Task<AcquirerResponse> CaptureAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
        FollowUpAsync("capture", "sim_cap_", request, cancellationToken);

    public Task<AcquirerResponse> RefundAsync(FollowUpRequest request, CancellationToken cancellationToken) =>
        FollowUpAsync("refund", "sim_ref_", request, cancellationToken);

    private async Task<AcquirerResponse> FollowUpAsync(
        string operation, string referencePrefix, FollowUpRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = Start(operation, request.PaymentId.Value);
        await WaitForNetworkAsync(cancellationToken);

        var expected = AuthorizationPrefix + request.PaymentId.Value.ToString("N");
        var response = string.Equals(request.AuthorizationReference, expected, StringComparison.Ordinal)
            ? AcquirerResponse.Approved(referencePrefix + request.PaymentId.Value.ToString("N"))
            : AcquirerResponse.Declined("authorization_not_found");
        return Finish(activity, response);
    }

    private Task WaitForNetworkAsync(CancellationToken cancellationToken) =>
        _latency == TimeSpan.Zero ? Task.CompletedTask : Task.Delay(_latency, timeProvider, cancellationToken);

    // The tags go in at the start, so a sampler or a listener sees them as the span begins.
    private static Activity? Start(string operation, Guid paymentId) =>
        ApplicationTelemetry.ActivitySource.StartActivity(
            $"acquirer {operation}",
            ActivityKind.Client,
            parentContext: default,
            tags: [new("payment.id", paymentId), new("acquirer.name", "simulated")]);

    private static AcquirerResponse Finish(Activity? activity, AcquirerResponse response)
    {
        activity?.SetTag("acquirer.decision", response.Decision.ToString());
        if (response.Decision == AcquirerDecision.Unavailable)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "acquirer unavailable");
        }

        return response;
    }
}
