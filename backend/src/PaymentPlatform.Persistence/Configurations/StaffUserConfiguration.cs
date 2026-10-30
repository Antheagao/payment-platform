using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Staff;

namespace PaymentPlatform.Persistence.Configurations;

internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.ToTable("StaffUsers");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id)
            .HasConversion(id => id.Value, value => new StaffUserId(value))
            .ValueGeneratedNever();

        // Sign-in looks staff up by email, case-insensitively through the database's collation, as for merchant users.
        builder.Property(user => user.Email).HasMaxLength(MerchantUser.EmailMaxLength).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(MerchantUserConfiguration.PasswordHashMaxLength)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(user => user.CreatedAt).IsRequired();
    }
}
