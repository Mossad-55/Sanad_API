using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class PaymobSubscriptionIdentityConfiguration : IEntityTypeConfiguration<PaymobSubscriptionIdentity>
{
    public void Configure(EntityTypeBuilder<PaymobSubscriptionIdentity> builder)
    {
        builder.ToTable("paymob_subscription_identities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.ProviderSubscriptionId)
            .HasColumnName("provider_subscription_id")
            .HasMaxLength(100)
            .IsRequired();
        builder.Property(x => x.PaymentAttemptId).HasColumnName("payment_attempt_id").IsRequired();
        builder.Property(x => x.FamilySubscriptionId).HasColumnName("family_subscription_id");

        builder.HasIndex(x => x.ProviderSubscriptionId)
            .IsUnique()
            .HasDatabaseName("ux_paymob_subscription_identities_provider_subscription_id");

        builder.HasOne<SubscriptionPaymentAttempt>()
            .WithMany()
            .HasForeignKey(x => x.PaymentAttemptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FamilySubscription>()
            .WithMany()
            .HasForeignKey(x => x.FamilySubscriptionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
