namespace PaymentPlatform.Worker.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// The job runtime. Each job is registered here with <c>AddHostedService</c> as it lands: outbox dispatch
    /// and webhook delivery (T23), settlement (T24), and reconciliation of payments stuck in <c>Pending</c>.
    /// </summary>
    public static IServiceCollection AddWorkerJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<WorkerTelemetry>();
        return services;
    }
}
