using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Cms.Domain.Landing;

namespace Ahlena.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class FamilyHomeSectionHistoryConfiguration :
    IEntityTypeConfiguration<FamilyHomeSectionHistory>
{
    public void Configure(EntityTypeBuilder<FamilyHomeSectionHistory> builder)
    {
        builder.ToTable("family_home_section_history");

        builder.HasKey(row => row.Id);

        builder.Property(row => row.Id)
            .HasConversion(
                id => id.Value,
                value => new FamilyHomeSectionHistoryId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(row => row.SectionId)
            .HasConversion(
                id => id.Value,
                value => new FamilyHomeSectionId(value))
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
            .HasMaxLength(FamilyHomeSectionHistory.MaximumDetailsLength);

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