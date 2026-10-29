using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentPlatform.Domain.Merchants;
using PaymentPlatform.Domain.Payments;

namespace PaymentPlatform.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        // Append-only and taken oldest first: an identity key keeps inserts at the end and the queue in order.
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).UseIdentityColumn();
        builder.HasIndex(message => message.EventId).IsUnique();

        // The queue is the undispatched rows only, so the dispatcher's claim reads a small index, not the history.
        builder.HasIndex(message => message.Id)
            .HasFilter("[DispatchedAt] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Undispatched");

        builder.Property(message => message.MerchantId).HasConversion(id => id.Value, value => new MerchantId(value));
        builder.Property(message => message.PaymentId).HasConversion(id => id.Value, value => new PaymentId(value));
        builder.HasOne<Payment>().WithMany().HasForeignKey(message => message.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(message => message.EventType).HasMaxLength(OutboxMessage.EventTypeMaxLength).IsUnicode(false);
        builder.Property(message => message.Payload).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(64).IsUnicode(false);
        builder.Property(message => message.TraceParent).HasMaxLength(OutboxMessage.TraceParentMaxLength).IsUnicode(false);
        builder.Property(message => message.OccurredAt);
    }
}

internal sealed class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("WebhookEndpoints");
        builder.HasKey(endpoint => endpoint.Id);
        builder.Property(endpoint => endpoint.Id).ValueGeneratedNever();
        builder.Property(endpoint => endpoint.MerchantId).HasConversion(id => id.Value, value => new MerchantId(value));
        builder.HasOne<Merchant>().WithMany().HasForeignKey(endpoint => endpoint.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // One endpoint per merchant (assumption 11).
        builder.HasIndex(endpoint => endpoint.MerchantId).IsUnique();
        builder.Property(endpoint => endpoint.Url).HasMaxLength(WebhookEndpoint.UrlMaxLength);
        builder.Property(endpoint => endpoint.Secret).HasMaxLength(WebhookEndpoint.SecretMaxLength).IsUnicode(false);
        builder.Property(endpoint => endpoint.CreatedAt);
    }
}

internal sealed class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        var statuses = string.Join(", ", Enum.GetNames<WebhookDeliveryStatus>().Select(name => $"'{name}'"));
        builder.ToTable("WebhookDeliveries", table => table.HasCheckConstraint("CK_WebhookDeliveries_Status", $"[Status] IN ({statuses})"));
        builder.HasKey(delivery => delivery.Id);
        builder.Property(delivery => delivery.Id).UseIdentityColumn();
        builder.Property(delivery => delivery.MerchantId).HasConversion(id => id.Value, value => new MerchantId(value));
        builder.HasOne<OutboxMessage>().WithMany().HasForeignKey(delivery => delivery.OutboxMessageId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<WebhookEndpoint>().WithMany().HasForeignKey(delivery => delivery.EndpointId).OnDelete(DeleteBehavior.Restrict);

        // One delivery per event and endpoint, however often a dispatch is retried.
        builder.HasIndex(delivery => new { delivery.OutboxMessageId, delivery.EndpointId }).IsUnique();

        // The delivery job's queue: pending rows by when they are next due.
        builder.HasIndex(delivery => delivery.NextAttemptAt)
            .HasFilter("[Status] = 'Pending'")
            .HasDatabaseName("IX_WebhookDeliveries_Due");
        builder.Property(delivery => delivery.Status).HasConversion<string>().HasMaxLength(20).IsUnicode(false);
        builder.Property(delivery => delivery.LastError).HasMaxLength(WebhookDelivery.LastErrorMaxLength);

        // A worker saves an attempt only while its lease stands: if the lease ran out and another worker took the
        // delivery, the first worker's save fails instead of overwriting the second's.
        builder.Property(delivery => delivery.LeasedUntil).IsConcurrencyToken();
        builder.Property(delivery => delivery.CreatedAt);
    }
}
