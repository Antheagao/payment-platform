using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence.Configurations;

internal sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKey>
{
    public const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<IdempotencyKey> builder)
    {
        builder.ToTable("IdempotencyKeys", table => table.HasCheckConstraint(
            "CK_IdempotencyKeys_Status",
            $"[Status] IN ({string.Join(", ", Enum.GetNames<IdempotencyStatus>().Select(name => $"'{name}'"))})"));

        // The unique key is the lock (ADR 007): of two concurrent first requests, only one insert succeeds.
        builder.HasKey(key => new { key.MerchantId, key.Key });
        builder.Property(key => key.MerchantId)
            .HasConversion(id => id.Value, value => new MerchantId(value));
        builder.HasOne<Merchant>().WithMany().HasForeignKey(key => key.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // Keys compare exactly: "abc" and "ABC" are two keys, which the default case-insensitive collation would merge.
        builder.Property(key => key.Key)
            .HasMaxLength(IdempotencyKey.KeyMaxLength)
            .IsUnicode(false)
            .UseCollation("Latin1_General_100_BIN2");

        builder.Property(key => key.Fingerprint).HasColumnType("binary(32)").IsRequired();
        builder.Property(key => key.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(key => key.PaymentId)
            .HasConversion(new ValueConverter<PaymentId, Guid>(id => id.Value, value => new PaymentId(value)));
        builder.Property(key => key.ResponseBody);
        builder.Property(key => key.ResponseLocation).HasMaxLength(IdempotencyKey.ResponseLocationMaxLength).IsUnicode(false);
        builder.Property(key => key.CreatedAt).IsRequired();
        builder.Property(key => key.ClaimedAt).IsRequired();
        builder.Property(key => key.ExpiresAt).IsRequired();

        // Taking over an abandoned or expired key races other retries; the loser's save fails on this token.
        builder.Property<byte[]>(RowVersion).IsRowVersion().IsRequired();

        // The cleanup job deletes expired keys in batches, oldest first.
        builder.HasIndex(key => key.ExpiresAt);
    }
}
