using System.ComponentModel.DataAnnotations;

namespace PaymentPlatform.Application.Acquiring;

/// <summary>Bound from <c>Acquirer:Simulated</c> and validated at startup.</summary>
public sealed class SimulatedAcquirerOptions
{
    public const string Section = "Acquirer:Simulated";

    /// <summary>A pretend network round trip, so traces show where acquirer time goes.</summary>
    [Range(0, 5_000)]
    public int LatencyMilliseconds { get; set; } = 80;
}
