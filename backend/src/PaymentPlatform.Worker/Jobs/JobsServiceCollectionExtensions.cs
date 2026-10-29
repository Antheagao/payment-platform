namespace PaymentPlatform.Worker.Jobs;

public static class JobsServiceCollectionExtensions
{
    /// <summary>
    /// The job runtime and its jobs, each registered with <c>AddHostedService</c> as it lands: the idempotency key
    /// cleanup, outbox dispatch, webhook delivery, and settlement today; reconciliation of payments stuck in
    /// <c>Pending</c> next.
    /// </summary>
    public static IServiceCollection AddWorkerJobs(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<WorkerTelemetry>();
        services.AddHostedService<DeleteExpiredIdempotencyKeysJob>();
        services.AddHostedService<DispatchOutboxJob>();
        services.AddHostedService<DeliverWebhooksJob>();
        services.AddHostedService<SettlePaymentsJob>();
        services.AddHostedService<MeasureQueuesJob>();
        return services;
    }
}
