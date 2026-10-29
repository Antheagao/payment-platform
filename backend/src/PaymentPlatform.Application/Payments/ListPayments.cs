using System.Buffers.Binary;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;
using PaymentPlatform.Persistence;

namespace PaymentPlatform.Application.Payments;

/// <summary>
/// A page request as the query string carried it: every value still text, so each bad one gets its own field
/// code instead of the binder's generic 400.
/// </summary>
public sealed record ListPaymentsRequest(
    string? Limit,
    string? Cursor,
    IReadOnlyList<string>? Status,
    string? CreatedFrom,
    string? CreatedTo,
    string? AmountMinMinor,
    string? AmountMaxMinor,
    string? Currency,
    string? Reference)
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;

    /// <summary>Parses and checks every parameter; on success the query to run, otherwise field errors as codes.</summary>
    public (ListPaymentsQuery? Query, IReadOnlyDictionary<string, string[]> Errors) Parse()
    {
        var errors = new Dictionary<string, string[]>();
        var limit = DefaultLimit;
        if (Limit is not null && (!int.TryParse(Limit, NumberStyles.None, CultureInfo.InvariantCulture, out limit) || limit is < 1 or > MaxLimit))
        {
            errors["limit"] = ["invalid_limit"];
        }

        PageCursor? cursor = null;
        if (Cursor is not null && (cursor = PageCursor.Decode(Cursor)) is null)
        {
            errors["cursor"] = ["invalid_cursor"];
        }

        var statuses = new List<PaymentStatus>();
        foreach (var name in (Status ?? []).SelectMany(value => value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)))
        {
            if (Enum.TryParse<PaymentStatus>(name, ignoreCase: true, out var status) && Enum.IsDefined(status) && !name.Any(char.IsDigit))
            {
                statuses.Add(status);
            }
            else
            {
                errors["status"] = ["status_invalid"];
            }
        }

        var from = ParseInstant(CreatedFrom, "created_from", errors);
        var to = ParseInstant(CreatedTo, "created_to", errors);
        if (from is not null && to is not null && from >= to)
        {
            errors["created_to"] = ["created_range_invalid"];
        }

        var amountMin = ParseAmount(AmountMinMinor, "amount_min_minor", errors);
        var amountMax = ParseAmount(AmountMaxMinor, "amount_max_minor", errors);
        if (amountMin is not null && amountMax is not null && amountMin > amountMax)
        {
            errors["amount_max_minor"] = ["amount_range_invalid"];
        }

        Currency? currency = null;
        if (Currency is not null)
        {
            var parsed = Domain.Monetary.Currency.Parse(Currency);
            if (parsed.IsSuccess)
            {
                currency = parsed.Value;
            }
            else
            {
                errors["currency"] = [parsed.Error.Code];
            }
        }

        var reference = Reference?.Trim();
        if (Reference is not null && (reference!.Length == 0 || reference.Length > Payment.MerchantReferenceMaxLength))
        {
            errors["reference"] = ["reference_invalid"];
        }

        return errors.Count > 0
            ? (null, errors)
            : (new ListPaymentsQuery(limit, cursor, statuses.Distinct().ToList(), from, to, amountMin, amountMax, currency, reference), errors);
    }

    private static DateTimeOffset? ParseInstant(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (text is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
        {
            return instant.ToUniversalTime();
        }

        errors[field] = [$"{field}_invalid"];
        return null;
    }

    private static long? ParseAmount(string? text, string field, Dictionary<string, string[]> errors)
    {
        if (text is null)
        {
            return null;
        }

        if (long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) && amount <= Money.MaxAmountMinor)
        {
            return amount;
        }

        errors[field] = [$"{field}_invalid"];
        return null;
    }
}

/// <summary>A parsed, valid page request: filters that all apply, a page size, and where the previous page ended.</summary>
public sealed record ListPaymentsQuery(
    int Limit,
    PageCursor? After,
    IReadOnlyList<PaymentStatus> Statuses,
    DateTimeOffset? CreatedFrom,
    DateTimeOffset? CreatedTo,
    long? AmountMinMinor,
    long? AmountMaxMinor,
    Currency? Currency,
    string? ReferencePrefix);

/// <summary>
/// Where a page ended: the last payment's creation time and insertion sequence, the keyset every page is ordered
/// by. Opaque to clients (base64url); a cursor only says where to resume, never what to show, so a forged one can
/// do no more than start from another position within the caller's own payments.
/// </summary>
public sealed record PageCursor(DateTimeOffset CreatedAt, long Sequence)
{
    private const int Size = sizeof(long) * 2;

    public string Encode()
    {
        Span<byte> bytes = stackalloc byte[Size];
        BinaryPrimitives.WriteInt64BigEndian(bytes, CreatedAt.UtcTicks);
        BinaryPrimitives.WriteInt64BigEndian(bytes[sizeof(long)..], Sequence);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static PageCursor? Decode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 32)
        {
            return null;
        }

        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - (base64.Length % 4)) % 4);
        Span<byte> bytes = stackalloc byte[Size + 3];
        if (!Convert.TryFromBase64String(base64, bytes, out var written) || written != Size)
        {
            return null;
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(bytes);
        var sequence = BinaryPrimitives.ReadInt64BigEndian(bytes[sizeof(long)..]);
        return ticks is < 0 || ticks > DateTimeOffset.MaxValue.UtcTicks || sequence < 0
            ? null
            : new PageCursor(new DateTimeOffset(ticks, TimeSpan.Zero), sequence);
    }
}

/// <summary>A payment in a list: enough to find it and see where it stands, without its history.</summary>
public sealed record PaymentSummary(
    PaymentId Id,
    PaymentStatus Status,
    long AmountMinor,
    string Currency,
    string? MerchantReference,
    CardBrand CardBrand,
    string CardLast4,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PaymentPage(IReadOnlyList<PaymentSummary> Data, string? NextCursor);

/// <summary>
/// One merchant's payments, newest first, filtered and keyset-paginated (api-design overlay): each page resumes
/// after the last row of the one before by <c>(CreatedAt, Sequence)</c>, never by offset, so pages stay fast and a
/// payment created meanwhile neither repeats nor skips a row. The order is the clustered index's, read backwards.
/// </summary>
public sealed class ListPayments(PaymentsDbContext db)
{
    public async Task<PaymentPage> HandleAsync(MerchantId merchantId, ListPaymentsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var payments = db.Payments.AsNoTracking().Where(payment => payment.MerchantId == merchantId);
        if (query.After is { } after)
        {
            payments = payments.Where(payment => payment.CreatedAt < after.CreatedAt
                || (payment.CreatedAt == after.CreatedAt && EF.Property<long>(payment, PaymentsDbContext.PaymentSequence) < after.Sequence));
        }

        if (query.Statuses.Count > 0)
        {
            payments = payments.Where(payment => query.Statuses.Contains(payment.Status));
        }

        if (query.CreatedFrom is { } from)
        {
            payments = payments.Where(payment => payment.CreatedAt >= from);
        }

        if (query.CreatedTo is { } to)
        {
            payments = payments.Where(payment => payment.CreatedAt < to);
        }

        if (query.AmountMinMinor is { } min)
        {
            payments = payments.Where(payment => payment.Amount.AmountMinor >= min);
        }

        if (query.AmountMaxMinor is { } max)
        {
            payments = payments.Where(payment => payment.Amount.AmountMinor <= max);
        }

        if (query.Currency is { } currency)
        {
            payments = payments.Where(payment => payment.Amount.Currency == currency);
        }

        if (query.ReferencePrefix is { } prefix)
        {
            payments = payments.Where(payment => payment.MerchantReference != null && payment.MerchantReference.StartsWith(prefix));
        }

        // One row more than the page, to know whether another page follows.
        var rows = await payments
            .OrderByDescending(payment => payment.CreatedAt)
            .ThenByDescending(payment => EF.Property<long>(payment, PaymentsDbContext.PaymentSequence))
            .Take(query.Limit + 1)
            .Select(payment => new
            {
                Summary = new PaymentSummary(
                    payment.Id,
                    payment.Status,
                    payment.Amount.AmountMinor,
                    payment.Amount.Currency.Code,
                    payment.MerchantReference,
                    payment.Card.Brand,
                    payment.Card.Last4,
                    payment.CreatedAt,
                    payment.UpdatedAt),
                Sequence = EF.Property<long>(payment, PaymentsDbContext.PaymentSequence),
            })
            .ToListAsync(cancellationToken);

        var page = rows.Take(query.Limit).ToList();
        var next = rows.Count > query.Limit ? new PageCursor(page[^1].Summary.CreatedAt, page[^1].Sequence).Encode() : null;
        return new PaymentPage(page.Select(row => row.Summary).ToList(), next);
    }
}
