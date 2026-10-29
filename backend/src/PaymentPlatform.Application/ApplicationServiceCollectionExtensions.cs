using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PaymentPlatform.Application.Acquiring;
using PaymentPlatform.Application.Auth;
using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Application;

public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the use cases and the adapters for external systems. Today the acquirer is always the
    /// simulated one; a real adapter would be chosen here from configuration.
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

        services.TryAddSingleton<IPasswordHasher<MerchantUser>, PasswordHasher<MerchantUser>>();
        services.AddSingleton<PasswordVerifier>();
        services.AddScoped<LogIn>();
        services.AddScoped<GetCurrentUser>();
        return services;
    }
}
