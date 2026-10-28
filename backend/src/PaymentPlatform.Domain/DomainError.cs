namespace PaymentPlatform.Domain;

/// <summary>
/// A failure the caller is expected to handle, such as an invalid transition or an invalid amount.
/// </summary>
/// <param name="Code">The stable snake_case code the API returns and clients switch on (ADR 006); never renamed.</param>
/// <param name="Message">A short message that is safe to show to a merchant.</param>
public sealed record DomainError(string Code, string Message);
