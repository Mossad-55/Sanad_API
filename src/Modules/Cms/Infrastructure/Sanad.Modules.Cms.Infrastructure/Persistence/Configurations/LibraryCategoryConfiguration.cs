using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class LibraryCategoryConfiguration : IEntityTypeConfiguration<LibraryCategory>
{
    public void Configure(EntityTypeBuilder<LibraryCategory> builder)
    {
        builder.ToTable("library_categories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id)
            .HasColumnName("id")
            .ValueGeneratedNever()
            .HasConversion(id => id.Value, value => new LibraryCategoryId(value));
        builder.Property(x => x.ArabicName)
            .HasColumnName("arabic_name")
            .HasMaxLength(LibraryCategory.MaximumNameLength)
            .IsRequired();
        builder.Property(x => x.EnglishName)
            .HasColumnName("english_name")
            .HasMaxLength(LibraryCategory.MaximumNameLength)
            .IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedOnUtc).HasColumnName("created_on_utc").IsRequired();
        builder.Property(x => x.UpdatedOnUtc).HasColumnName("updated_on_utc");
        builder.Ignore(x => x.DomainEvents);
    }
}
