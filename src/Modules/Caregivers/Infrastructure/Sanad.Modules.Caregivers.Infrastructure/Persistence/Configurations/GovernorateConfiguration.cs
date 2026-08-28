using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

public sealed class GovernorateConfiguration :
    IEntityTypeConfiguration<Governorate>
{
    public void Configure(EntityTypeBuilder<Governorate> builder)
    {
        builder.ToTable("governorates");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id)
            .HasConversion(id => id.Value, value => new GovernorateId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();
        builder.Property(g => g.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(Governorate.MaximumNameLength)
            .IsRequired();
        builder.Property(g => g.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(Governorate.MaximumNameLength)
            .IsRequired();
        builder.Property(g => g.IsActive)
            .HasColumnName("is_active")
            .IsRequired();
        builder.Property(g => g.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();
        builder.Property(g => g.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();
        builder.Ignore(g => g.DomainEvents);
    }
}