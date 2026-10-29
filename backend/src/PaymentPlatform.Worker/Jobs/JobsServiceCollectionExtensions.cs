namespace PaymentPlatform.Worker.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// The job runtime and its jobs, each registered with <c>AddHostedService</c> as it lands: the idempotency key
    /// cleanup and outbox dispatch today; webhook delivery (T23c), settlement (T24), and reconciliation of payments
    /// stuck in <c>Pending</c> next.
    /// </summary>
    public static IServiceCollection AddWorkerJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<WorkerTelemetry>();
        services.AddHostedService<DeleteExpiredIdempotencyKeysJob>();
        services.AddHostedService<DispatchOutboxJob>();
        return services;
    }
}
