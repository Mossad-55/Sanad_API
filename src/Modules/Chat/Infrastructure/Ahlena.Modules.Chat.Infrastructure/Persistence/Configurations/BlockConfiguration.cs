using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Blocks;

namespace Ahlena.Modules.Chat.Infrastructure.Persistence.Configurations;

public class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.ToTable("Blocks", ChatDbContext.Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new BlockId(value));
        builder.Property(e => e.BlockerUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.BlockedUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Ignore(e => e.DomainEvents);
        builder.HasIndex(e => new { e.BlockerUserId, e.BlockedUserId })
            .IsUnique();
        builder.HasIndex(e => e.BlockerUserId);
    }
}
