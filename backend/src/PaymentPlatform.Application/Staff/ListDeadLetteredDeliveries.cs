using System.Buffers.Binary;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Staff;

/// <summary>The list's query string as the API received it; <see cref="Parse"/> checks every value.</summary>
public sealed record ListDeadLetteredDeliveriesRequest(string? Status, string? Limit, string? Cursor)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 100;

    /// <summary>
    /// The one status this list answers for: the rest of the queue is the worker's, and the queue gauges show it.
    /// Asking for it by name keeps the route open to other statuses later.
    /// </summary>
    public const string DeadLettered = "dead_lettered";

    /// <summary>The page size and where to start, or field errors as codes.</summary>
    public (int Limit, long? Before, IReadOnlyDictionary<string, string[]> Errors) Parse()
    {
        var errors = new Dictionary<string, string[]>();
        if (Status is null)
        {
            errors["status"] = ["status_required"];
        }
        else if (Status != DeadLettered)
        {
            errors["status"] = ["invalid_status"];
        }

        var limit = DefaultLimit;
        if (Limit is not null && (!int.TryParse(Limit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
        {
            errors["limit"] = ["invalid_limit"];
        }

        long? before = null;
        if (Cursor is not null && (before = DeliveryCursor.Decode(Cursor)) is null)
        {
            errors["cursor"] = ["invalid_cursor"];
        }

        return (limit, before, errors);
    }
}

/// <summary>Where a page ended: the last delivery's id, newest first. Opaque to clients (base64url).</summary>
public static class DeliveryCursor
{
    public static string Encode(long id)
    {
        Span<byte> bytes = stackalloc byte[sizeof(long)];
        BinaryPrimitives.WriteInt64BigEndian(bytes, id);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static long? Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 16)
        {
            return null;
        }

        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - (base64.Length % 4)) % 4);
        Span<byte> bytes = stackalloc byte[sizeof(long) + 3];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != sizeof(long))
        {
            return null;
        }

        var id = BinaryPrimitives.ReadInt64BigEndian(bytes);
        return id > 0 ? id : null;
    }
}

/// <summary>
/// A delivery that gave up, with what an operator needs to decide on a replay: whose it is, which event, how it
/// failed, and where it was going, by host only, because a merchant's URL may carry a token in its query.
/// </summary>
public sealed record DeadLetteredDelivery(
    long Id,
    MerchantId MerchantId,
    PaymentId PaymentId,
    string EventType,
    int Attempts,
    int? LastStatusCode,
    string? LastError,
    string EndpointHost,
    DateTimeOffset CreatedAt);

public sealed record DeadLetteredDeliveryPage(IReadOnlyList<DeadLetteredDelivery> Data, string? NextCursor);

/// <summary>Every merchant's dead-lettered webhook deliveries, newest first, a page at a time (ADR 011).</summary>
public sealed class ListDeadLetteredDeliveries(PaymentsDbContext db)
{
    public async Task<DeadLetteredDeliveryPage> HandleAsync(int limit, long? before, CancellationToken cancellationToken)
    {
        var deliveries = db.WebhookDeliveries.AsNoTracking().Where(delivery => delivery.Status == WebhookDeliveryStatus.DeadLettered);
        if (before is { } id)
        {
            deliveries = deliveries.Where(delivery => delivery.Id < id);
        }

        var rows = await deliveries
            .OrderByDescending(delivery => delivery.Id)
            .Take(limit + 1)
            .Join(db.OutboxMessages, delivery => delivery.OutboxMessageId, message => message.Id, (delivery, message) => new { delivery, message })
            .Join(db.WebhookEndpoints, row => row.delivery.EndpointId, endpoint => endpoint.Id, (row, endpoint) => new { row.delivery, row.message, endpoint.Url })
            .OrderByDescending(row => row.delivery.Id)
            .ToListAsync(cancellationToken);

        var page = rows.Take(limit).Select(row => new DeadLetteredDelivery(
            row.delivery.Id,
            row.delivery.MerchantId,
            row.message.PaymentId,
            row.message.EventType,
            row.delivery.Attempts,
            row.delivery.LastStatusCode,
            row.delivery.LastError,
            new Uri(row.Url).Host,
            row.delivery.CreatedAt)).ToList();
        var next = rows.Count > limit ? DeliveryCursor.Encode(page[^1].Id) : null;
        return new DeadLetteredDeliveryPage(page, next);
    }
}
