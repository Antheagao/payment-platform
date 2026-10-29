namespace PaymentPlatform.Worker.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// The job runtime and its jobs, each registered with <c>AddHostedService</c> as it lands: the idempotency key
    /// cleanup today; outbox dispatch and webhook delivery (T23), settlement (T24), and reconciliation of payments
    /// stuck in <c>Pending</c> next.
    /// </summary>
    public static IServiceCollection AddWorkerJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<WorkerTelemetry>();
        services.AddHostedService<DeleteExpiredIdempotencyKeysJob>();
        return services;
    }
}
