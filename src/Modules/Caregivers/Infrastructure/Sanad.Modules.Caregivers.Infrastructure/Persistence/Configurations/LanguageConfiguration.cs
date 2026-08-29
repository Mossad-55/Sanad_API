using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class LanguageConfiguration :
    IEntityTypeConfiguration<Language>
{
    public void Configure(EntityTypeBuilder<Language> builder)
    {
        builder.ToTable("languages");
        builder.HasKey(language => language.Id);
        builder.Property(language => language.Id)
            .HasConversion(id => id.Value, value => new LanguageId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(language => language.Code)
            .HasColumnName("code")
            .HasMaxLength(Language.MaximumCodeLength)
            .IsRequired();
        builder.HasIndex(language => language.Code).IsUnique();
        builder.Property(language => language.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(Language.MaximumNameLength)
            .IsRequired();
        builder.Property(language => language.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(Language.MaximumNameLength)
            .IsRequired();
        builder.Property(language => language.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(language => language.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(language => language.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(language => language.DomainEvents);
    }
}