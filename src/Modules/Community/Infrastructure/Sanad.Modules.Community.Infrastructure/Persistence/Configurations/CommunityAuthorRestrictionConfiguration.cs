using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Posts;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public class CommunityAuthorRestrictionConfiguration : IEntityTypeConfiguration<CommunityAuthorRestriction>
{
    public void Configure(EntityTypeBuilder<CommunityAuthorRestriction> builder)
    {
        builder.ToTable("CommunityAuthorRestrictions", CommunityDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityAuthorRestrictionId(value));
        builder.Property(e => e.AuthorId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.ModeratorId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.Duration)
            .HasConversion<int>()
            .IsRequired();
        builder.Property(e => e.ReasonCode)
            .HasMaxLength(CommunityAuthorRestriction.MaximumReasonCodeLength)
            .IsRequired();
        builder.Property(e => e.Note)
            .HasMaxLength(CommunityAuthorRestriction.MaximumNoteLength)
            .IsRequired(false);
        builder.Property(e => e.StartsOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.ExpiresOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.RevokedBy)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : (UserId?)null)
            .IsRequired(false);
        builder.Property(e => e.RevokedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Property(e => e.RevocationReason)
            .HasMaxLength(CommunityAuthorRestriction.MaximumRevocationReasonLength)
            .IsRequired(false);

        // Active-restriction lookup (author, unrevoked, unexpired) and the
        // author-with-active-restriction counter. Multiple retained rows per
        // author are allowed; at most one may be active and that is enforced
        // by the command handler, not by a unique index.
        builder.HasIndex(e => e.AuthorId);
        builder.HasIndex(e => e.ExpiresOnUtc);
        builder.HasIndex(e => e.RevokedOnUtc);
    }
}
