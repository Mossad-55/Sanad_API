using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class CaregiverPayoutAccountReviewConfiguration :
    IEntityTypeConfiguration<CaregiverPayoutAccountReview>
{
    public void Configure(EntityTypeBuilder<CaregiverPayoutAccountReview> builder)
    {
        builder.ToTable("caregiver_payout_account_reviews");
        builder.HasKey(review => review.Id);
        builder.Property(review => review.Id)
            .HasConversion(id => id.Value, value => new CaregiverPayoutAccountReviewId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(review => review.PayoutAccountId)
            .HasConversion(id => id.Value, value => new CaregiverPayoutAccountId(value))
            .HasColumnName("payout_account_id")
            .IsRequired();
        builder.HasIndex(review => review.PayoutAccountId)
            .HasDatabaseName("ix_caregiver_payout_account_reviews_account");
        builder.HasOne<CaregiverPayoutAccount>()
            .WithMany()
            .HasForeignKey(review => review.PayoutAccountId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(review => review.AccountRevision)
            .HasColumnName("account_revision")
            .IsRequired();
        builder.Property(review => review.Decision)
            .HasColumnName("decision")
            .IsRequired();
        builder.Property(review => review.ActorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("actor_user_id")
            .IsRequired();
        builder.Property(review => review.OccurredOnUtc)
            .HasColumnName("occurred_on_utc")
            .IsRequired();
        builder.Property(review => review.VerificationSource)
            .HasColumnName("verification_source")
            .HasMaxLength(CaregiverPayoutAccount.MaximumSourceLength);
        builder.Property(review => review.Reference)
            .HasColumnName("reference")
            .HasMaxLength(CaregiverPayoutAccount.MaximumReferenceLength);
        builder.Property(review => review.Reason)
            .HasColumnName("reason")
            .HasMaxLength(CaregiverPayoutAccount.MaximumReasonLength);
        builder.Ignore(review => review.DomainEvents);
    }
}
