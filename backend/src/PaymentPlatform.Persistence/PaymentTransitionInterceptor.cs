using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace PaymentPlatform.Persistence;

/// <summary>
/// Counts the status changes a save writes, once the save has committed: a save that fails, or loses a race and is
/// retried, counts nothing until the save that succeeds. Only rows recorded as news count, so the demo seed's
/// history does not show as a burst of activity at startup.
/// </summary>
internal sealed class PaymentTransitionInterceptor(PaymentMetrics metrics) : SaveChangesInterceptor
{
    // One list per context while its save is in flight; the interceptor is shared by every pooled context.
    private readonly ConditionalWeakTable<DbContext, List<PaymentEvent>> _saving = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Collect(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Collect(eventData);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Record(eventData);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Record(eventData);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Forget(eventData);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData);
        return Task.CompletedTask;
    }

    private void Collect(DbContextEventData eventData)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        var news = context.ChangeTracker.Entries<PaymentEvent>()
            .Where(entry => entry.State == EntityState.Added && entry.Entity.IsNews)
            .Select(entry => entry.Entity)
            .ToList();
        _saving.AddOrUpdate(context, news);
    }

    private void Record(DbContextEventData eventData)
    {
        if (eventData.Context is { } context && _saving.TryGetValue(context, out var saved))
        {
            _saving.Remove(context);
            foreach (var group in saved.GroupBy(row => (row.FromStatus, row.ToStatus)))
            {
                metrics.RecordTransitions(group.Key.FromStatus, group.Key.ToStatus, group.Count());
            }
        }
    }

    private void Forget(DbContextEventData eventData)
    {
        if (eventData.Context is { } context)
        {
            _saving.Remove(context);
        }
    }
}
