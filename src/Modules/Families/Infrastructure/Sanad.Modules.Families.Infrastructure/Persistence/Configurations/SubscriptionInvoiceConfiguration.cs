using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
    {
        builder.ToTable("subscription_invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.InvoiceNumber).HasColumnName("invoice_number").HasMaxLength(20).IsRequired();
        builder.Property(x => x.OwnerFamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(x => x.PaymentAttemptId).HasColumnName("payment_attempt_id");
        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(x => x.PlanKey).HasColumnName("plan_key").HasMaxLength(100).IsRequired();
        builder.Property(x => x.PlanVersion).HasColumnName("plan_version").IsRequired();
        builder.Property(x => x.BasePrice).HasColumnName("base_price").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.DiscountAmount).HasColumnName("discount_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TaxAmount).HasColumnName("tax_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.TotalPayable).HasColumnName("total_payable").HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PeriodStartsOnUtc).HasColumnName("period_starts_on_utc").IsRequired();
        builder.Property(x => x.PeriodEndsOnUtc).HasColumnName("period_ends_on_utc").IsRequired();
        builder.Property(x => x.IssuedOnUtc).HasColumnName("issued_on_utc").IsRequired();
        builder.Property(x => x.PdfStorageKey).HasColumnName("pdf_storage_key").HasMaxLength(300).IsRequired();
        builder.Property(x => x.ProviderEventId).HasColumnName("provider_event_id").HasMaxLength(200);
        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.HasIndex(x => x.PaymentAttemptId).IsUnique().HasFilter("payment_attempt_id IS NOT NULL");
        builder.HasIndex(x => x.ProviderEventId).IsUnique().HasFilter("provider_event_id IS NOT NULL");
        builder.HasOne<FamilySubscription>().WithMany().HasForeignKey(x => x.SubscriptionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SubscriptionPaymentAttempt>().WithMany().HasForeignKey(x => x.PaymentAttemptId).OnDelete(DeleteBehavior.Restrict);
    }
}
