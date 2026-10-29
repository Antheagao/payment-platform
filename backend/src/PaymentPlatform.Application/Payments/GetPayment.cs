using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Payments;

/// <summary>
/// One payment with its audit trail, scoped to the caller's merchant: another merchant's payment is
/// indistinguishable from one that does not exist (docs/assumptions.md, assumption 2).
/// </summary>
public sealed class GetPayment(PaymentsDbContext db)
{
    public async Task<PaymentDetails?> HandleAsync(PaymentId id, MerchantId merchantId, CancellationToken cancellationToken)
    {
        var payment = await db.Payments.AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Id == id && candidate.MerchantId == merchantId, cancellationToken);
        if (payment is null)
        {
            return null;
        }

        var events = await db.PaymentEvents.AsNoTracking()
            .Where(row => row.PaymentId == id)
            .OrderBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return PaymentDetails.From(payment, events);
    }
}
