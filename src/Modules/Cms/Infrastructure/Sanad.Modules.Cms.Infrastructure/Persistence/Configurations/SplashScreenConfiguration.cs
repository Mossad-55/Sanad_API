using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class SplashScreenConfiguration :
    IEntityTypeConfiguration<SplashScreen>
{
    public void Configure(
        EntityTypeBuilder<SplashScreen> builder)
    {
        builder.ToTable("splash_screens");

        builder.HasKey(screen => screen.Id);

        builder.Property(screen => screen.Id)
            .HasConversion(
                id => id.Value,
                value => new SplashScreenId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(screen => screen.InternalName)
            .HasColumnName("internal_name")
            .HasMaxLength(SplashScreen.MaximumInternalNameLength)
            .IsRequired();

        builder.HasIndex(screen => screen.InternalName)
            .IsUnique();

        builder.Property(screen => screen.ArabicTitle)
            .HasColumnName("arabic_title")
            .HasMaxLength(SplashScreen.MaximumTitleLength)
            .IsRequired();

        builder.Property(screen => screen.EnglishTitle)
            .HasColumnName("english_title")
            .HasMaxLength(SplashScreen.MaximumTitleLength)
            .IsRequired();

        builder.Property(screen => screen.ArabicDescription)
            .HasColumnName("arabic_description")
            .HasMaxLength(SplashScreen.MaximumDescriptionLength)
            .IsRequired();

        builder.Property(screen => screen.EnglishDescription)
            .HasColumnName("english_description")
            .HasMaxLength(SplashScreen.MaximumDescriptionLength)
            .IsRequired();

        builder.Property(screen => screen.ArabicButtonText)
            .HasColumnName("arabic_button_text")
            .HasMaxLength(SplashScreen.MaximumButtonTextLength)
            .IsRequired();

        builder.Property(screen => screen.EnglishButtonText)
            .HasColumnName("english_button_text")
            .HasMaxLength(SplashScreen.MaximumButtonTextLength)
            .IsRequired();

        builder.Property(screen => screen.ImagePath)
            .HasColumnName("image_path")
            .HasMaxLength(SplashScreen.MaximumImagePathLength)
            .IsRequired();

        builder.Property(screen => screen.BackgroundColor)
            .HasColumnName("background_color")
            .HasMaxLength(SplashScreen.BackgroundColorLength)
            .IsRequired();

        builder.Property(screen => screen.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(screen => screen.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(screen => screen.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(screen => screen.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(screen => new
        {
            screen.Status,
            screen.DisplayOrder
        });

        builder.Ignore(screen => screen.DomainEvents);
    }
}