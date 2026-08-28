using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class ServiceConfiguration :
    IEntityTypeConfiguration<Service>
{
    public void Configure(EntityTypeBuilder<Service> builder)
    {
        builder.ToTable("services");
        builder.HasKey(service => service.Id);
        builder.Property(service => service.Id)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(service => service.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(Service.MaximumNameLength)
            .IsRequired();
        builder.Property(service => service.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(Service.MaximumNameLength)
            .IsRequired();
        builder.Property(service => service.CaregiverType)
            .HasColumnName("caregiver_type")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(service => service.IconPath)
            .HasColumnName("icon_path")
            .HasMaxLength(Service.MaximumIconPathLength)
            .IsRequired();
        builder.Property(service => service.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(service => service.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(service => service.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.HasIndex(service => service.CaregiverType);
        builder.Ignore(service => service.DomainEvents);
    }
}