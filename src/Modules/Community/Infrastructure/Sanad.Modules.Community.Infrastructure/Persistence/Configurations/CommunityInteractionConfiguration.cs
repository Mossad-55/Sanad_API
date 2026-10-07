using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;

namespace Sanad.Modules.Community.Infrastructure.Persistence.Configurations;

public sealed class CommunityInteractionConfiguration : IEntityTypeConfiguration<CommunityInteraction>
{
    public void Configure(EntityTypeBuilder<CommunityInteraction> builder)
    {
        builder.ToTable("Interactions", CommunityDbContext.Schema);
        builder.HasKey(interaction => interaction.Id);
        builder.Property(interaction => interaction.Id)
            .HasConversion(id => id.Value, value => new CommunityInteractionId(value));
        builder.Property(interaction => interaction.UserId)
            .HasConversion(id => id.Value, value => new UserId(value));
        builder.Property(interaction => interaction.TargetType).HasConversion<int>();
        builder.Property(interaction => interaction.Kind).HasConversion<int>();
        builder.Property(interaction => interaction.CreatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
        builder.HasIndex(interaction => new
        {
            interaction.TargetType,
            interaction.TargetId,
            interaction.UserId,
            interaction.Kind
        }).IsUnique();
    }
}
