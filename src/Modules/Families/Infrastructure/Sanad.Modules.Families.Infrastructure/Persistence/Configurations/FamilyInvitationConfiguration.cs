using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Invitations;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class FamilyInvitationConfiguration :
    IEntityTypeConfiguration<FamilyInvitation>
{
    public void Configure(
        EntityTypeBuilder<FamilyInvitation> builder)
    {
        builder.ToTable("family_invitations");

        builder.HasKey(invitation => invitation.Id);

        builder.Property(invitation => invitation.Id)
            .HasConversion(
                id => id.Value,
                value => new FamilyInvitationId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(invitation => invitation.FamilyId)
            .HasConversion(
                id => id.Value,
                value => new FamilyId(value))
            .HasColumnName("family_id")
            .IsRequired();

        builder.Property(invitation => invitation.InvitedEmail)
            .HasConversion(
                email => email.Value,
                value => Email.Create(value))
            .HasMaxLength(256)
            .HasColumnName("invited_email")
            .IsRequired();

        builder.Property(invitation => invitation.InvitedUserId)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("invited_user_id")
            .IsRequired();

        builder.Property(invitation => invitation.Role)
            .HasConversion<int>()
            .HasColumnName("role")
            .IsRequired();

        builder.Property(invitation => invitation.RelationshipType)
            .HasConversion<int>()
            .HasColumnName("relationship_type")
            .IsRequired();

        builder.Property(invitation => invitation.TokenHash)
            .HasMaxLength(FamilyInvitation.MaximumTokenHashLength)
            .HasColumnName("token_hash")
            .IsRequired();

        builder.Property(invitation => invitation.Status)
            .HasConversion<int>()
            .HasColumnName("status")
            .IsRequired();

        builder.Property(invitation => invitation.CreatedByUserId)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("created_by_user_id")
            .IsRequired();

        builder.Property(invitation => invitation.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(invitation => invitation.ExpiresOnUtc)
            .HasColumnName("expires_on_utc")
            .IsRequired();

        builder.Property(invitation => invitation.DecidedOnUtc)
            .HasColumnName("decided_on_utc");

        // Token lookups (accept/decline) must resolve a single, unique row.
        builder.HasIndex(invitation => invitation.TokenHash)
            .IsUnique();

        builder.HasIndex(invitation => invitation.FamilyId);

        builder.HasIndex(invitation => invitation.InvitedUserId);

        builder.Ignore(invitation => invitation.DomainEvents);
    }
}