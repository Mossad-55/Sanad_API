using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class LandingSectionHistoryConfiguration :
    IEntityTypeConfiguration<LandingSectionHistory>
{
    public void Configure(EntityTypeBuilder<LandingSectionHistory> builder)
    {
        builder.ToTable("landing_section_history");

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .HasConversion(
                id => id.Value,
                value => new LandingSectionHistoryId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(row => row.SectionId)
            .HasConversion(
                id => id.Value,
                value => new LandingSectionId(value))
            .HasColumnName("section_id")
            .IsRequired();

        builder.Property(row => row.Action)
            .HasColumnName("action")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(row => row.ActorUserId)
            .HasConversion(
                id => id.Value,
                value => new UserId(value))
            .HasColumnName("actor_user_id")
            .IsRequired();

        builder.Property(row => row.Details)
            .HasColumnName("details")
            .HasMaxLength(LandingSectionHistory.MaximumDetailsLength);

        builder.Property(row => row.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.HasIndex(row => new
        {
            row.SectionId,
            row.CreatedOnUtc
        });

        builder.Ignore(row => row.DomainEvents);
    }
}
