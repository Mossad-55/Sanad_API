using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Authentication;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration :
    IEntityTypeConfiguration<User>
{
    public void Configure(
        EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .ValueGeneratedNever();

        builder.Property(user => user.ArabicFullName)
            .HasConversion(
                value => value.Value,
                value => FullName.Create(value))
            .HasMaxLength(200)
            .HasColumnName("arabic_full_name")
            .IsRequired();

        builder.Property(user => user.EnglishFullName)
            .HasConversion(
                value => value.Value,
                value => FullName.Create(value))
            .HasMaxLength(200)
            .HasColumnName("english_full_name")
            .IsRequired();

        builder.Property(user => user.DateOfBirth)
            .HasColumnName("date_of_birth");

        builder.Property(user => user.Gender)
            .HasConversion<int?>()
            .HasColumnName("gender");

        builder.Property(user => user.Email)
            .HasConversion(
                value => value == null
                    ? null
                    : value.Value,
                value => value == null
                    ? null
                    : Email.Create(value))
            .HasMaxLength(256)
            .HasColumnName("email");

        builder.Property(user => user.PhoneNumber)
            .HasConversion(
                value => value.Value,
                value => PhoneNumber.Create(value))
            .HasMaxLength(16)
            .HasColumnName("phone_number")
            .IsRequired();

        builder.OwnsOne(
            user => user.Password,
            password =>
            {
                password.Property(value =>
                        value.PasswordHash)
                    .HasColumnName("password_hash")
                    .HasMaxLength(
                        PasswordCredential
                            .MaximumHashLength);
            });

        builder.Property(user => user.AvatarUrl)
            .HasColumnName("avatar_url")
            .HasMaxLength(500);

        builder.Property(user => user.EmailVerified)
            .HasColumnName("email_verified")
            .IsRequired();

        builder.Property(user => user.PhoneVerified)
            .HasColumnName("phone_verified")
            .IsRequired();

        builder.Property(user => user.Status)
            .HasConversion<int>()
            .HasColumnName("status")
            .IsRequired();

        builder.Property(user => user.StatusReason)
            .HasColumnName("status_reason")
            .HasMaxLength(
                User.MaximumStatusReasonLength);

        builder.Property(user => user.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(user => user.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.Property(user => user.LastLoginOnUtc)
            .HasColumnName("last_login_on_utc");

        builder.HasIndex(user => user.Email)
            .IsUnique()
            .HasFilter("email IS NOT NULL");

        builder.HasIndex(user => user.PhoneNumber)
            .IsUnique();

        builder.HasIndex(user => user.Status);

        builder.OwnsMany(
            user => user.Accounts,
            account =>
            {
                account.ToTable("user_accounts");

                account.WithOwner()
                    .HasForeignKey("UserId");

                account.Property<UserId>("UserId")
                    .HasConversion(
                        id => id.Value,
                        value => new UserId(value))
                    .HasColumnName("user_id")
                    .IsRequired();

                account.HasKey(value => value.Id);

                account.Property(value => value.Id)
                    .HasColumnName("id")
                    .ValueGeneratedNever();

                account.Property(value =>
                        value.AccountType)
                    .HasColumnName("account_type")
                    .HasConversion<int>()
                    .IsRequired();

                account.Property(value =>
                        value.CreatedOnUtc)
                    .HasColumnName("created_on_utc")
                    .IsRequired();

                account.HasIndex(
                    "UserId",
                    nameof(UserAccount.AccountType))
                .IsUnique();
            });

        builder.OwnsMany(
            user => user.ExternalLogins,
            login =>
            {
                login.ToTable("user_external_logins");

                login.WithOwner()
                    .HasForeignKey("UserId");

                login.Property<UserId>("UserId")
                    .HasConversion(
                        id => id.Value,
                        value => new UserId(value))
                    .HasColumnName("user_id")
                    .IsRequired();

                login.HasKey(value => value.Id);

                login.Property(value => value.Id)
                    .HasConversion(
                        id => id.Value,
                        value =>
                            new UserExternalLoginId(
                                value))
                    .HasColumnName("id")
                    .ValueGeneratedNever();

                login.Property(value =>
                        value.Provider)
                    .HasColumnName("provider")
                    .HasConversion<int>()
                    .IsRequired();

                login.Property(value =>
                        value.ProviderSubject)
                    .HasColumnName("provider_subject")
                    .HasMaxLength(
                        UserExternalLogin
                            .MaximumProviderSubjectLength)
                    .IsRequired();

                login.Property(value =>
                        value.LinkedOnUtc)
                    .HasColumnName("linked_on_utc")
                    .IsRequired();

                login.HasIndex(
                        value => new
                        {
                            value.Provider,
                            value.ProviderSubject
                        })
                    .IsUnique();

                login.HasIndex(
                    "UserId",
                    nameof(UserExternalLogin.Provider))
                .IsUnique();
            });

        builder.OwnsOne(
            user => user.IdentityDocument,
            document =>
            {
                document.ToTable(
                    "user_identity_documents");

                document.WithOwner()
                    .HasForeignKey("UserId");

                document.Property<UserId>("UserId")
                    .HasConversion(
                        id => id.Value,
                        value => new UserId(value))
                    .HasColumnName("user_id")
                    .IsRequired();

                document.HasKey(value => value.Id);

                document.Property(value => value.Id)
                    .HasConversion(
                        id => id.Value,
                        value =>
                            new UserIdentityDocumentId(
                                value))
                    .HasColumnName("id")
                    .ValueGeneratedNever();

                document.Property(value =>
                        value.FrontImagePath)
                    .HasColumnName("front_image_path")
                    .HasMaxLength(
                        UserIdentityDocument
                            .MaximumImagePathLength)
                    .IsRequired();

                document.Property(value =>
                        value.BackImagePath)
                    .HasColumnName("back_image_path")
                    .HasMaxLength(
                        UserIdentityDocument
                            .MaximumImagePathLength)
                    .IsRequired();

                document.Property(value =>
                        value.VerificationStatus)
                    .HasColumnName(
                        "verification_status")
                    .HasConversion<int>()
                    .IsRequired();

                document.Property(value =>
                        value.ReviewReason)
                    .HasColumnName("review_reason")
                    .HasMaxLength(
                        UserIdentityDocument
                            .MaximumReviewReasonLength);

                document.Property(value =>
                        value.CreatedOnUtc)
                    .HasColumnName("created_on_utc")
                    .IsRequired();

                document.Property(value =>
                        value.UpdatedOnUtc)
                    .HasColumnName("updated_on_utc")
                    .IsRequired();

                document.HasIndex("UserId")
                    .IsUnique();
            });

        builder.Ignore(user => user.HasPassword);
        builder.Ignore(user => user.HasExternalLogin);
        builder.Ignore(user => user.DomainEvents);
    }
}