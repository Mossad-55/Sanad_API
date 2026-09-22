using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Families.Domain.Subscriptions;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionTaxRuleConfiguration : IEntityTypeConfiguration<SubscriptionTaxRule>
{
    public void Configure(EntityTypeBuilder<SubscriptionTaxRule> builder)
    {
        builder.ToTable("subscription_tax_rules", tableBuilder =>
        {
            tableBuilder.HasCheckConstraint(
                "ck_subscription_tax_rules_rate_percentage",
                "\"rate_percentage\" BETWEEN 0 AND 100");
            tableBuilder.HasCheckConstraint(
                "ck_subscription_tax_rules_version_positive",
                "\"version\" > 0");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.RatePercentage).HasColumnName("rate_percentage").HasPrecision(5, 2).IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.EffectiveOnUtc).HasColumnName("effective_on_utc").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.HasIndex(x => x.Version)
            .IsUnique()
            .HasDatabaseName("ux_subscription_tax_rules_version");
        builder.HasIndex(x => x.IsActive)
            .HasFilter("\"is_active\" = TRUE")
            .IsUnique()
            .HasDatabaseName("ux_subscription_tax_rules_active");
    }
}
