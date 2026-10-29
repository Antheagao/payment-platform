using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.IntegrationTests.Support;

/// <summary>A merchant and one dashboard user, created straight in the test's database.</summary>
public sealed record TestUser(MerchantId MerchantId, MerchantUserId UserId, string Email, string Password, string MerchantName);

public static class Merchants
{
    /// <summary>
    /// Creates a merchant with one user whose password is hashed by the API's own hasher, unless the test passes
    /// a hash it made some other way.
    /// </summary>
    public static async Task<TestUser> CreateUserAsync(
        WebApplicationFactory<Program> factory,
        string email,
        string password = "correct horse battery staple",
        string merchantName = "Test Merchant",
        string? passwordHash = null)
    {
        ArgumentNullException.ThrowIfNull(factory);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<PasswordVerifier>();
        var now = TimeProvider.System.GetUtcNow();

        var merchant = Merchant.Create(new MerchantId(Guid.CreateVersion7(now)), merchantName, now);
        var user = MerchantUser.Create(
            MerchantUserId.New(now), merchant.Id, email, passwordHash ?? passwords.Hash(password), now);
        db.Merchants.Add(merchant);
        db.MerchantUsers.Add(user);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return new TestUser(merchant.Id, user.Id, email, password, merchantName);
    }
}
