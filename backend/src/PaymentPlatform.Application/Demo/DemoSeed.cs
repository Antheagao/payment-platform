using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Demo;

/// <summary>
/// Sample data for local runs and demos (<c>Demo:Seed</c>, on in compose only): a demo merchant and its
/// dashboard user. The host refuses the setting in Production; the rows have fixed ids, so seeding again on every
/// start adds nothing.
/// </summary>
/// <remarks>
/// The demo login is public on purpose, like the compose database passwords: it opens a local-only stack and
/// is shown in docs/demo.md and, in Development, on the dashboard's login page.
/// </remarks>
public static partial class DemoSeed
{
    public const string SettingName = "Demo:Seed";

    public const string MerchantName = "Demo Merchant";

    public const string Email = "demo@example.com";

    public const string Password = "LocalOnly_Demo_2026!";

    public static readonly MerchantId MerchantId = new(Guid.Parse("019a2c00-0000-7000-8000-0000000000d1"));

    public static readonly MerchantUserId UserId = new(Guid.Parse("019a2c00-0000-7000-8000-0000000000d2"));

    /// <summary>Adds whichever demo rows are missing; expects a migrated database.</summary>
    public static async Task RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordVerifier>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DemoSeed));

        var added = 0;
        if (!await db.Merchants.AnyAsync(merchant => merchant.Id == MerchantId, cancellationToken))
        {
            db.Merchants.Add(Merchant.Create(MerchantId, MerchantName, now));
            added++;
        }

        if (!await db.MerchantUsers.AnyAsync(user => user.Id == UserId, cancellationToken))
        {
            db.MerchantUsers.Add(MerchantUser.Create(UserId, MerchantId, Email, passwords.Hash(Password), now));
            added++;
        }

        await db.SaveChangesAsync(cancellationToken);
        LogSeeded(logger, MerchantId, added);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Demo data for merchant {MerchantId} seeded: {AddedCount} rows added")]
    private static partial void LogSeeded(ILogger logger, MerchantId merchantId, int addedCount);
}
