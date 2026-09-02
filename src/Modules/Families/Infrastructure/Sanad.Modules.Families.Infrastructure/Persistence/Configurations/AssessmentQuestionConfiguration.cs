using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class AssessmentQuestionConfiguration :
    IEntityTypeConfiguration<AssessmentQuestion>
{
    public void Configure(EntityTypeBuilder<AssessmentQuestion> builder)
    {
        builder.ToTable("assessment_questions");

        builder.HasKey(q => q.Id);

        builder.Property(q => q.Id)
            .HasConversion(
                id => id.Value,
                value => new AssessmentQuestionId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(q => q.Order)
            .HasColumnName("order")
            .IsRequired();

        builder.Property(q => q.ArabicText)
            .HasColumnName("arabic_text")
            .HasMaxLength(AssessmentQuestion.MaximumTextLength)
            .IsRequired();

        builder.Property(q => q.EnglishText)
            .HasColumnName("english_text")
            .HasMaxLength(AssessmentQuestion.MaximumTextLength)
            .IsRequired();

        builder.Property(q => q.IsRequired)
            .HasColumnName("is_required")
            .IsRequired();

        builder.Property(q => q.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(q => q.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(q => q.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(q => new { q.IsActive, q.Order });

        builder.OwnsMany(
            q => q.Options,
            option =>
            {
                option.ToTable("assessment_options");

                option.WithOwner()
                    .HasForeignKey("QuestionId");

                option.Property<AssessmentQuestionId>("QuestionId")
                    .HasConversion(
                        id => id.Value,
                        value => new AssessmentQuestionId(value))
                    .HasColumnName("question_id")
                    .IsRequired();

                option.Property(o => o.Id)
                    .HasConversion(
                        id => id.Value,
                        value => new AssessmentOptionId(value))
                    .HasColumnName("id")
                    .ValueGeneratedNever();

                option.HasKey("Id");

                option.Property(o => o.Order)
                    .HasColumnName("order")
                    .IsRequired();

                option.Property(o => o.ArabicText)
                    .HasColumnName("arabic_text")
                    .HasMaxLength(AssessmentOption.MaximumTextLength)
                    .IsRequired();

                option.Property(o => o.EnglishText)
                    .HasColumnName("english_text")
                    .HasMaxLength(AssessmentOption.MaximumTextLength)
                    .IsRequired();

                option.Property(o => o.Weight)
                    .HasColumnName("weight")
                    .IsRequired();

                option.HasIndex("QuestionId", "Order");
            });

        builder.Ignore(q => q.DomainEvents);
    }
}