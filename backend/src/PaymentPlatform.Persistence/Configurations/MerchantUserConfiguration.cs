using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentPlatform.Domain.Merchants;

namespace PaymentPlatform.Persistence.Configurations;

internal sealed class MerchantUserConfiguration : IEntityTypeConfiguration<MerchantUser>
{
    /// <summary>
    /// ASP.NET Core Identity's version 3 hash is 84 base64 characters; the column leaves room for a format
    /// with a longer salt or subkey without a migration.
    /// </summary>
    public const int PasswordHashMaxLength = 256;

    public void Configure(EntityTypeBuilder<MerchantUser> builder)
    {
        builder.ToTable("MerchantUsers");

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id)
            .HasConversion(id => id.Value, value => new MerchantUserId(value))
            .ValueGeneratedNever();
        builder.Property(user => user.MerchantId)
            .HasConversion(id => id.Value, value => new MerchantId(value))
            .IsRequired();

        // Login looks users up by email. The database's default collation is case-insensitive, so the unique
        // index refuses Demo@Example.com beside demo@example.com and the lookup ignores case for free.
        builder.Property(user => user.Email).HasMaxLength(MerchantUser.EmailMaxLength).IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();

        builder.Property(user => user.PasswordHash)
            .HasMaxLength(PasswordHashMaxLength)
            .IsUnicode(false)
            .IsRequired();
        builder.Property(user => user.CreatedAt).IsRequired();

        builder.HasOne<Merchant>()
            .WithMany()
            .HasForeignKey(user => user.MerchantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(user => user.MerchantId);
    }
}
