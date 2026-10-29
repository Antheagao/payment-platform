using System.ComponentModel.DataAnnotations;

namespace PaymentPlatform.Api.Auth;

/// <summary>
/// What the API takes from the merchant dashboard (ADR 010), from the <c>Dashboard</c> section. The dashboard signs
/// people in from its own server, so the API sees the dashboard's address rather than theirs; a request that carries
/// this key may name the person's address instead. The key is a secret: Key Vault in Azure (<c>Dashboard__Key</c>,
/// infra/), and a public local-only value in compose.
/// </summary>
public sealed class DashboardOptions
{
    public const string Section = "Dashboard";

    /// <summary>Long enough that guessing it is out of the question.</summary>
    public const int MinimumKeyLength = 32;

    /// <summary>Unset, no request may name its client's address, and every login is limited by its connection.</summary>
    [MinLength(MinimumKeyLength)]
    public string? Key { get; set; }
}
