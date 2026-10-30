using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Staff;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Staff;

/// <summary>The signed-in staff user, as the dashboard's operator area shows them.</summary>
public sealed record CurrentStaff(StaffUserId StaffId, string Email);

/// <summary>Looks up the staff user a validated token names; one that no longer exists is no one.</summary>
public sealed class GetCurrentStaff(PaymentsDbContext db)
{
    public Task<CurrentStaff?> HandleAsync(StaffUserId staffId, CancellationToken cancellationToken) =>
        db.StaffUsers.AsNoTracking()
            .Where(staff => staff.Id == staffId)
            .Select(staff => new CurrentStaff(staff.Id, staff.Email))
            .SingleOrDefaultAsync(cancellationToken);
}
