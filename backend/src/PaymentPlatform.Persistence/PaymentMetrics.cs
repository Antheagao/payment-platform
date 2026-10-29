using System.Diagnostics.Metrics;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence;

/// <summary>
/// The <c>PaymentPlatform.Payments</c> meter (R19): every payment status change once it is saved, by the status it
/// left (<c>none</c> for a new payment) and the one it entered, so created, authorized, failed, captured, settled, and
/// refunded payments are all one counter, sliced by its tags.
/// </summary>
public sealed class PaymentMetrics
{
    public const string MeterName = "PaymentPlatform.Payments";

    public const string FromTag = "payment.status.from";

    public const string ToTag = "payment.status.to";

    private readonly Counter<long> _transitions;

    public PaymentMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _transitions = meterFactory.Create(MeterName).CreateCounter<long>(
            "payments.transitions", unit: "{transition}", description: "Payment status changes saved, by the status left and the status entered.");
    }

    public void RecordTransitions(PaymentStatus? from, PaymentStatus to, long count = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (count == 0)
        {
            return;
        }

        _transitions.Add(
            count,
            new KeyValuePair<string, object?>(FromTag, from?.ToString().ToLowerInvariant() ?? "none"),
            new KeyValuePair<string, object?>(ToTag, to.ToString().ToLowerInvariant()));
    }
}
