using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Monetary;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments", table =>
        {
            table.HasCheckConstraint("CK_Payments_Status", StatusCheck.Sql("Status"));
            table.HasCheckConstraint("CK_Payments_AmountMinor", $"[AmountMinor] > 0 AND [AmountMinor] <= {Money.MaxAmountMinor}");
            table.HasCheckConstraint("CK_Payments_CardBrand", $"[CardBrand] IN ({string.Join(", ", Enum.GetNames<CardBrand>().Select(name => $"'{name}'"))})");
        });

        // GUIDs do not sort by time in SQL Server, so the primary key is nonclustered and the table clusters on
        // the access path every list takes: one merchant's payments, newest first (database overlay).
        builder.HasKey(payment => payment.Id).IsClustered(false);
        builder.HasIndex(payment => new { payment.MerchantId, payment.CreatedAt, payment.Id }).IsUnique().IsClustered();

        builder.Property(payment => payment.Id)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .ValueGeneratedNever();
        builder.Property(payment => payment.MerchantId)
            .HasConversion(id => id.Value, value => new MerchantId(value))
            .IsRequired();
        builder.HasOne<Merchant>().WithMany().HasForeignKey(payment => payment.MerchantId).OnDelete(DeleteBehavior.Restrict);

        builder.ComplexProperty(payment => payment.Amount, amount =>
        {
            amount.Property(money => money.AmountMinor).HasColumnName("AmountMinor").IsRequired();
            amount.Property(money => money.Currency)
                .HasColumnName("Currency")
                .HasConversion(currency => currency.Code, code => Currency.Parse(code).Value)
                .HasMaxLength(3)
                .IsFixedLength()
                .IsUnicode(false)
                .IsRequired();
        });

        builder.ComplexProperty(payment => payment.Card, card =>
        {
            card.Property(value => value.Token).HasColumnName("CardToken").HasMaxLength(64).IsUnicode(false).IsRequired();
            card.Property(value => value.Brand).HasColumnName("CardBrand").HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
            card.Property(value => value.Last4).HasColumnName("CardLast4").HasMaxLength(4).IsFixedLength().IsUnicode(false).IsRequired();
            card.Property(value => value.ExpiryMonth).HasColumnName("CardExpiryMonth").HasColumnType("tinyint").IsRequired();
            card.Property(value => value.ExpiryYear).HasColumnName("CardExpiryYear").HasColumnType("smallint").IsRequired();
        });

        builder.Property(payment => payment.MerchantReference).HasMaxLength(Payment.MerchantReferenceMaxLength);
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(StatusCheck.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(payment => payment.AuthorizationReference).HasMaxLength(64).IsUnicode(false);
        builder.Property(payment => payment.FailureReason).HasMaxLength(PaymentEvent.ReasonMaxLength).IsUnicode(false);
        builder.Property(payment => payment.CreatedAt).IsRequired();
        builder.Property(payment => payment.UpdatedAt).IsRequired();

        // A change saved over a newer one fails instead of overwriting it; the domain never sees the column.
        builder.Property<byte[]>(RowVersion).IsRowVersion().IsRequired();

        builder.Ignore(payment => payment.PendingEvents);
    }
}

/// <summary>The <c>CHECK</c> for a status column, generated from the enum so a new status is one migration.</summary>
internal static class StatusCheck
{
    public const int MaxLength = 20;

    public static string Sql(string column) =>
        $"[{column}] IN ({string.Join(", ", Enum.GetNames<PaymentStatus>().Select(name => $"'{name}'"))})";
}
