using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ahlena.Modules.Community.Domain.Categories;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Community.Infrastructure.Persistence.Configurations;

public class CommunityCategoryConfiguration : IEntityTypeConfiguration<CommunityCategory>
{
    public void Configure(EntityTypeBuilder<CommunityCategory> builder)
    {
        builder.ToTable("CommunityCategories", "community");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasConversion(id => id.Value, value => new CommunityCategoryId(value));
        builder.Property(e => e.ArabicName).HasMaxLength(CommunityCategory.MaximumNameLength);
        builder.Property(e => e.EnglishName).HasMaxLength(CommunityCategory.MaximumNameLength);
        builder.Property(e => e.IsActive);
        builder.Property(e => e.CreatedOnUtc)
            .HasColumnType("timestamp with time zone");
        builder.Property(e => e.UpdatedOnUtc)
            .HasColumnType("timestamp with time zone")
            .IsRequired(false);
        builder.Ignore(e => e.DomainEvents);
    }
}
