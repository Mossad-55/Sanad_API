using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Finance.Domain;

namespace Sanad.Modules.Finance.Infrastructure.Persistence.Configurations;

/// <summary>
/// The cumulative refundable budget of one CAPTURE — one succeeded payment
/// attempt and the paid period it bought (Owner decisions #4 and #6). One row
/// per capture is the serialization point every clause-9 case against that
/// capture claims against, so a duplicate-charge case and a shutdown case can
/// never both refund the same captured money.
/// <para>
/// The scope is the capture, not the subscription, because the ceiling is the
/// money paid for <em>that</em> period. A subscription-scoped budget seeded with
/// lifetime captures lets an older, already-consumed period supply headroom for
/// the current one. <c>subscription_id</c> is kept as a non-unique audit index so
/// all of a subscription's budgets remain reachable for reconciliation without
/// widening any one ceiling.
/// </para>
/// <para>
/// <c>version</c> is the optimistic concurrency token (platform-control-state
/// precedent): two concurrent claims that both read the same claimed total
/// cannot both commit, because the second update fails its
/// <c>WHERE version = @original</c> predicate and surfaces
/// <c>DbUpdateConcurrencyException</c> — which the recorder maps to the stable
/// retryable conflict. The unique index on <c>payment_attempt_id</c> is the
/// second half of that guarantee: two first-time claims for one capture cannot
/// both insert a row.
/// </para>
/// </summary>
public sealed class SubscriptionRefundBudgetConfiguration : IEntityTypeConfiguration<SubscriptionRefundBudget>
{
    public void Configure(EntityTypeBuilder<SubscriptionRefundBudget> builder)
    {
        builder.ToTable("subscription_refund_budgets", table =>
        {
            table.HasCheckConstraint(
                "ck_subscription_refund_budgets_captured",
                "captured_minor_units >= 0");
            table.HasCheckConstraint(
                "ck_subscription_refund_budgets_claimed",
                "claimed_minor_units >= 0");
            // The invariant this row exists to enforce, at the database level too:
            // cumulative reservations can never exceed captured funds.
            table.HasCheckConstraint(
                "ck_subscription_refund_budgets_within_captured",
                "claimed_minor_units <= captured_minor_units");
            table.HasCheckConstraint(
                "ck_subscription_refund_budgets_version",
                "version > 0");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(x => x.PaymentAttemptId).HasColumnName("payment_attempt_id").IsRequired();
        builder.Property(x => x.SubscriptionId).HasColumnName("subscription_id").IsRequired();
        builder.Property(x => x.FamilyId).HasColumnName("family_id").IsRequired();
        builder.Property(x => x.CapturedMinorUnits).HasColumnName("captured_minor_units").IsRequired();
        builder.Property(x => x.ClaimedMinorUnits).HasColumnName("claimed_minor_units").IsRequired();
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc").IsRequired();

        // One budget per capture: the unique row is the shared serialization point
        // for every refund case that touches that capture.
        builder.HasIndex(x => x.PaymentAttemptId)
            .IsUnique()
            .HasDatabaseName("ux_subscription_refund_budgets_payment_attempt_id");
        // Audit correlation across a subscription's captures. Deliberately NOT
        // unique: uniqueness belongs to the capture, and a subscription owns many.
        builder.HasIndex(x => x.SubscriptionId)
            .HasDatabaseName("ix_subscription_refund_budgets_subscription_id");
        builder.HasIndex(x => x.FamilyId)
            .HasDatabaseName("ix_subscription_refund_budgets_family_id");
    }
}
