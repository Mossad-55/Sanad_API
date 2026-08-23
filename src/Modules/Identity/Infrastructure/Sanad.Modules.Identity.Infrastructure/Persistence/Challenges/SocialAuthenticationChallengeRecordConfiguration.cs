using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Infrastructure.Persistence.Challenges;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SocialAuthenticationChallengeRecordConfiguration :
    IEntityTypeConfiguration<
        SocialAuthenticationChallengeRecord>
{
    public void Configure(
        EntityTypeBuilder<
            SocialAuthenticationChallengeRecord> builder)
    {
        builder.ToTable(
            "social_authentication_challenges");

        builder.HasKey(record =>
            record.Id);

        builder.Property(record => record.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(record =>
                record.ChallengeHash)
            .HasColumnName("challenge_hash")
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(record =>
                record.Provider)
            .HasColumnName("provider")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(record =>
                record.ProviderSubject)
            .HasColumnName("provider_subject")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(record =>
                record.VerifiedEmail)
            .HasColumnName("verified_email")
            .HasMaxLength(256);

        builder.Property(record =>
                record.ExistingUserId)
            .HasConversion(
                id => id.HasValue
                    ? id.Value.Value
                    : (Guid?)null,
                value => value.HasValue
                    ? new UserId(value.Value)
                    : (UserId?)null)
            .HasColumnName("existing_user_id");

        builder.Property(record =>
                record.LinkVerificationRequestId)
            .HasConversion(
                id => id.HasValue
                    ? id.Value.Value
                    : (Guid?)null,
                value => value.HasValue
                    ? new VerificationRequestId(
                        value.Value)
                    : (VerificationRequestId?)null)
            .HasColumnName(
                "link_verification_request_id");

        builder.Property(record =>
                record.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(record =>
                record.ExpiresOnUtc)
            .HasColumnName("expires_on_utc")
            .IsRequired();

        builder.Property(record =>
                record.ConsumedOnUtc)
            .HasColumnName("consumed_on_utc");

        builder.HasIndex(record =>
                record.ChallengeHash)
            .IsUnique();

        builder.HasIndex(record =>
                new
                {
                    record.ExpiresOnUtc,
                    record.ConsumedOnUtc
                });
    }
}