using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class AcademicDegreeConfiguration :
    IEntityTypeConfiguration<AcademicDegree>
{
    public void Configure(EntityTypeBuilder<AcademicDegree> builder)
    {
        builder.ToTable("academic_degrees");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id)
            .HasConversion(id => id.Value, value => new AcademicDegreeId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(d => d.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(AcademicDegree.MaximumNameLength)
            .IsRequired();
        builder.Property(d => d.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(AcademicDegree.MaximumNameLength)
            .IsRequired();
        builder.Property(d => d.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(d => d.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(d => d.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(d => d.DomainEvents);
    }
}