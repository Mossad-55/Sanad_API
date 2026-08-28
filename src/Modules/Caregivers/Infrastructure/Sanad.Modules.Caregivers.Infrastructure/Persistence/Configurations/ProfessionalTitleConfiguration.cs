using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class ProfessionalTitleConfiguration :
    IEntityTypeConfiguration<ProfessionalTitle>
{
    public void Configure(EntityTypeBuilder<ProfessionalTitle> builder)
    {
        builder.ToTable("professional_titles");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasConversion(id => id.Value, value => new ProfessionalTitleId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(t => t.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(ProfessionalTitle.MaximumNameLength)
            .IsRequired();
        builder.Property(t => t.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(ProfessionalTitle.MaximumNameLength)
            .IsRequired();
        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(t => t.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(t => t.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(t => t.DomainEvents);
    }
}