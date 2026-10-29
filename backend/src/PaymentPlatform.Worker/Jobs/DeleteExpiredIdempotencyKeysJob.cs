using PaymentPlatform.Application.Idempotency;

namespace PaymentPlatform.Worker.Jobs;

/// <summary>
/// Deletes idempotency keys whose 24 hours are up (ADR 007), hourly. Keys are only ever read by merchant and key,
/// so a late cleanup costs storage, not correctness: an expired key is treated as new whether or not it is gone.
/// </summary>
public sealed partial class DeleteExpiredIdempotencyKeysJob(
    IServiceScopeFactory scopes, TimeProvider timeProvider, WorkerTelemetry telemetry, ILogger<DeleteExpiredIdempotencyKeysJob> logger)
    : PeriodicJob(scopes, timeProvider, telemetry, logger)
{
    public override string Name => "delete-expired-idempotency-keys";

    public override TimeSpan Interval => TimeSpan.FromHours(1);

    protected override async Task RunOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var deleted = await services.GetRequiredService<DeleteExpiredIdempotencyKeys>().HandleAsync(cancellationToken);
        if (deleted > 0)
        {
            LogDeleted(logger, deleted);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted {DeletedCount} expired idempotency keys")]
    private static partial void LogDeleted(ILogger logger, int deletedCount);
}
