using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Blocks;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public class CommunityAuthorBlockConfiguration : IEntityTypeConfiguration<CommunityAuthorBlock>
{
    public void Configure(EntityTypeBuilder<CommunityAuthorBlock> builder)
    {
        builder.ToTable("CommunityAuthorBlocks", CommunityDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityAuthorBlockId(value));
        builder.Property(e => e.BlockerUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.BlockedAuthorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Ignore(e => e.DomainEvents);

        // One relation per (viewer, author) pair, and the viewer's own rows
        // are looked up by the blocker column.
        builder.HasIndex(e => new { e.BlockerUserId, e.BlockedAuthorUserId })
            .IsUnique();
        builder.HasIndex(e => e.BlockerUserId);
    }
}
