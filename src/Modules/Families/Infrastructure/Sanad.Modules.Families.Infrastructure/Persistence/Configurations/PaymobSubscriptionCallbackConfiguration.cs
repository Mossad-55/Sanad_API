using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class PaymobSubscriptionCallbackConfiguration : IEntityTypeConfiguration<PaymobSubscriptionCallback>
{
    public void Configure(EntityTypeBuilder<PaymobSubscriptionCallback> builder)
    {
        builder.ToTable("paymob_subscription_callbacks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CallbackKey).HasColumnName("callback_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.PaymobRequestId).HasColumnName("paymob_request_id").HasMaxLength(200);
        builder.Property(x => x.ProviderSubscriptionId).HasColumnName("provider_subscription_id").HasMaxLength(100).IsRequired();
        builder.Property(x => x.TriggerType).HasColumnName("trigger_type").HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReceivedOnUtc).HasColumnName("received_on_utc").IsRequired();
        builder.HasIndex(x => x.CallbackKey).IsUnique().HasDatabaseName("ux_paymob_subscription_callbacks_key");
        builder.HasIndex(x => x.PaymobRequestId)
            .IsUnique()
            .HasFilter("paymob_request_id IS NOT NULL")
            .HasDatabaseName("ux_paymob_subscription_callbacks_request_id");
    }
}
