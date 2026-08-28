using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class CityConfiguration :
    IEntityTypeConfiguration<City>
{
    public void Configure(EntityTypeBuilder<City> builder)
    {
        builder.ToTable("cities");
        builder.HasKey(city => city.Id);
        builder.Property(city => city.Id)
            .HasConversion(id => id.Value, value => new CityId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(city => city.GovernorateId)
            .HasConversion(id => id.Value, value => new GovernorateId(value))
            .HasColumnName("governorate_id")
            .IsRequired();
        builder.HasIndex(city => city.GovernorateId);
        builder.Property(city => city.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(City.MaximumNameLength)
            .IsRequired();
        builder.Property(city => city.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(City.MaximumNameLength)
            .IsRequired();
        builder.Property(city => city.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(city => city.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(city => city.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(city => city.DomainEvents);
    }
}