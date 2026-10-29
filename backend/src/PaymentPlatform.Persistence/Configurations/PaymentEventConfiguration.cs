using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentPlatform.Domain;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence.Configurations;

internal sealed class PaymentEventConfiguration : IEntityTypeConfiguration<PaymentEvent>
{
    /// <summary>The API's limit for a correlation id (<c>CorrelationIds.MaxLength</c>, which Persistence may not reference).</summary>
    private const int CorrelationIdMaxLength = 64;

    public void Configure(EntityTypeBuilder<PaymentEvent> builder)
    {
        builder.ToTable("PaymentEvents", table =>
        {
            table.HasCheckConstraint("CK_PaymentEvents_FromStatus", $"[FromStatus] IS NULL OR {StatusCheck.Sql("FromStatus")}");
            table.HasCheckConstraint("CK_PaymentEvents_ToStatus", StatusCheck.Sql("ToStatus"));
            table.HasCheckConstraint("CK_PaymentEvents_ActorKind", $"[ActorKind] IN ({string.Join(", ", Enum.GetNames<ActorKind>().Select(name => $"'{name}'"))})");
        });

        // Append-only and read by payment: an identity key keeps inserts at the end of the table, and the index
        // returns one payment's trail in the order it was written.
        builder.HasKey(change => change.Id);
        builder.Property(change => change.Id).UseIdentityColumn();
        builder.HasIndex(change => new { change.PaymentId, change.Id });

        builder.Property(change => change.PaymentId)
            .HasConversion(id => id.Value, value => new PaymentId(value))
            .IsRequired();
        builder.Property(change => change.MerchantId)
            .HasConversion(id => id.Value, value => new MerchantId(value))
            .IsRequired();
        builder.HasOne<Payment>().WithMany().HasForeignKey(change => change.PaymentId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(change => change.FromStatus).HasConversion<string>().HasMaxLength(StatusCheck.MaxLength).IsUnicode(false);
        builder.Property(change => change.ToStatus).HasConversion<string>().HasMaxLength(StatusCheck.MaxLength).IsUnicode(false).IsRequired();
        builder.Property(change => change.ActorKind).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(change => change.ActorId).HasMaxLength(PaymentEvent.ActorIdMaxLength).IsRequired();
        builder.Property(change => change.OccurredAt).IsRequired();
        builder.Property(change => change.Reason).HasMaxLength(PaymentEvent.ReasonMaxLength).IsUnicode(false);
        builder.Property(change => change.CorrelationId).HasMaxLength(CorrelationIdMaxLength).IsUnicode(false);
    }
}
