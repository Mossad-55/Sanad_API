using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class AssessmentTierConfiguration :
    IEntityTypeConfiguration<AssessmentTier>
{
    public void Configure(EntityTypeBuilder<AssessmentTier> builder)
    {
        builder.ToTable("assessment_tiers");

        builder.HasKey(tier => tier.Id);

        builder.Property(tier => tier.Id)
            .HasConversion(
                id => id.Value,
                value => new AssessmentTierId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(tier => tier.ScreenOrder)
            .HasColumnName("screen_order")
            .IsRequired();

        builder.Property(tier => tier.ArabicTitle)
            .HasColumnName("arabic_title")
            .HasMaxLength(AssessmentTier.MaximumTitleLength)
            .IsRequired();

        builder.Property(tier => tier.EnglishTitle)
            .HasColumnName("english_title")
            .HasMaxLength(AssessmentTier.MaximumTitleLength)
            .IsRequired();

        builder.Property(tier => tier.ArabicSubtitle)
            .HasColumnName("arabic_subtitle")
            .HasMaxLength(AssessmentTier.MaximumSubtitleLength)
            .IsRequired();

        builder.Property(tier => tier.EnglishSubtitle)
            .HasColumnName("english_subtitle")
            .HasMaxLength(AssessmentTier.MaximumSubtitleLength)
            .IsRequired();

        builder.Property(tier => tier.BackgroundColor)
            .HasColumnName("background_color")
            .HasMaxLength(AssessmentTier.MaximumColorLength)
            .IsRequired();

        builder.Property(tier => tier.ArabicButtonText)
            .HasColumnName("arabic_button_text")
            .HasMaxLength(AssessmentTier.MaximumButtonTextLength)
            .IsRequired();

        builder.Property(tier => tier.EnglishButtonText)
            .HasColumnName("english_button_text")
            .HasMaxLength(AssessmentTier.MaximumButtonTextLength)
            .IsRequired();

        builder.Property(tier => tier.ImagePath)
            .HasColumnName("image_path")
            .HasMaxLength(AssessmentTier.MaximumImagePathLength)
            .IsRequired();

        builder.Property(tier => tier.MinScore)
            .HasColumnName("min_score")
            .IsRequired();

        builder.Property(tier => tier.MaxScore)
            .HasColumnName("max_score")
            .IsRequired();

        builder.Property(tier => tier.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(tier => tier.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(tier => tier.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        var stringListComparer = new ValueComparer<IReadOnlyList<string>>(
            (c1, c2) => (c1 == null && c2 == null) || (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList().AsReadOnly());

        builder.Property(tier => tier.ArabicRecommendations)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(tier => tier.ArabicRecommendations)
            .HasColumnName("arabic_recommendations")
            .IsRequired();

        builder.Property(tier => tier.EnglishRecommendations)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(tier => tier.EnglishRecommendations)
            .HasColumnName("english_recommendations")
            .IsRequired();

        builder.HasIndex(tier => new { tier.IsActive, tier.ScreenOrder });

        builder.Ignore(tier => tier.DomainEvents);
    }
}