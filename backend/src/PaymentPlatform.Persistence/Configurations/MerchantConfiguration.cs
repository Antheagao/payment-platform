using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Persistence.Configurations;

internal sealed class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.ToTable("Merchants");

        // A handful of rows, written once and read by id: clustering on the id costs nothing here, unlike on
        // Payments, which clusters on (MerchantId, CreatedAt, Id) (database overlay).
        builder.HasKey(merchant => merchant.Id);
        builder.Property(merchant => merchant.Id)
            .HasConversion(id => id.Value, value => new MerchantId(value))
            .ValueGeneratedNever();
        builder.Property(merchant => merchant.Name).HasMaxLength(Merchant.NameMaxLength).IsRequired();
        builder.Property(merchant => merchant.CreatedAt).IsRequired();
    }
}
