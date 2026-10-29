using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Application.Cards;
using PaymentPlatform.Application.Idempotency;
using PaymentPlatform.Application.Payments;
using PaymentPlatform.Application.Webhooks;
using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the use cases and the adapters for external systems. Today the acquirer and the card vault
    /// are always the simulated ones; real adapters would be chosen here from configuration.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<SimulatedAcquirerOptions>()
            .BindConfiguration(SimulatedAcquirerOptions.Section)
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IAcquirer, SimulatedAcquirer>();
        services.AddSingleton<ICardVault, SimulatedCardVault>();

        services.TryAddSingleton<IPasswordHasher<MerchantUser>, PasswordHasher<MerchantUser>>();
        services.AddSingleton<PasswordVerifier>();
        services.AddScoped<LogIn>();
        services.AddScoped<GetCurrentUser>();
        services.AddScoped<CreatePayment>();
        services.AddScoped<GetPayment>();
        services.AddScoped<ListPayments>();
        services.AddScoped<CapturePayment>();
        services.AddScoped<RefundPayment>();
        services.AddScoped<IdempotencyStore>();
        services.AddScoped<DeleteExpiredIdempotencyKeys>();
        services.AddScoped<DispatchOutboxMessages>();
        return services;
    }
}
