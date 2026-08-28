using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class AreaConfiguration :
    IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        builder.ToTable("areas");
        builder.HasKey(area => area.Id);
        builder.Property(area => area.Id)
            .HasConversion(id => id.Value, value => new AreaId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(area => area.CityId)
            .HasConversion(id => id.Value, value => new CityId(value))
            .HasColumnName("city_id")
            .IsRequired();
        builder.HasIndex(area => area.CityId);
        builder.Property(area => area.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(Area.MaximumNameLength)
            .IsRequired();
        builder.Property(area => area.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(Area.MaximumNameLength)
            .IsRequired();
        builder.Property(area => area.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(area => area.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(area => area.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(area => area.DomainEvents);
    }
}