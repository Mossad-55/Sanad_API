using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Infrastructure.Persistence.Challenges;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

internal sealed class SocialRegistrationChallengeRecordConfiguration :
    IEntityTypeConfiguration<
        SocialRegistrationChallengeRecord>
{
    public void Configure(
        EntityTypeBuilder<
            SocialRegistrationChallengeRecord> builder)
    {
        builder.ToTable(
            "social_registration_challenges");

        builder.HasKey(record =>
            record.Id);

        builder.Property(record =>
                record.Id)
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
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(record =>
                record.ArabicFullName)
            .HasColumnName("arabic_full_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(record =>
                record.EnglishFullName)
            .HasColumnName("english_full_name")
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(record =>
                record.AccountType)
            .HasColumnName("account_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(record =>
                record.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(record =>
                record.PhoneVerificationRequestId)
            .HasConversion(
                id => id.Value,
                value =>
                    new VerificationRequestId(
                        value))
            .HasColumnName(
                "phone_verification_request_id")
            .IsRequired();

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