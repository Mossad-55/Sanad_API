using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class SpecializationConfiguration :
    IEntityTypeConfiguration<Specialization>
{
    public void Configure(EntityTypeBuilder<Specialization> builder)
    {
        builder.ToTable("specializations");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasConversion(id => id.Value, value => new SpecializationId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(s => s.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(Specialization.MaximumNameLength)
            .IsRequired();
        builder.Property(s => s.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(Specialization.MaximumNameLength)
            .IsRequired();
        builder.Property(s => s.CaregiverType)
            .HasColumnName("caregiver_type")
            .HasConversion<int>()
            .IsRequired();
        builder.Property(s => s.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(s => s.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(s => s.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.HasIndex(s => s.CaregiverType);
        builder.Ignore(s => s.DomainEvents);
    }
}