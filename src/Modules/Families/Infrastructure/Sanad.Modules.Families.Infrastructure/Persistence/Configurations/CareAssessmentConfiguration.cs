using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Assessments;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class CareAssessmentConfiguration :
    IEntityTypeConfiguration<CareAssessment>
{
    public void Configure(EntityTypeBuilder<CareAssessment> builder)
    {
        builder.ToTable("care_assessments");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasConversion(
                id => id.Value,
                value => new CareAssessmentId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(a => a.FamilyId)
            .HasConversion(
                id => id.Value,
                value => new FamilyId(value))
            .HasColumnName("family_id")
            .IsRequired();

        builder.Property(a => a.ElderlyId)
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new ElderlyId(value.Value) : null)
            .HasColumnName("elderly_id");

        builder.Property(a => a.AssessmentTierId)
            .HasConversion(
                id => id.Value,
                value => new AssessmentTierId(value))
            .HasColumnName("assessment_tier_id")
            .IsRequired();

        builder.Property(a => a.TotalScore)
            .HasColumnName("total_score")
            .IsRequired();

        builder.Property(a => a.CompletedOnUtc)
            .HasColumnName("completed_on_utc")
            .IsRequired();

        builder.HasIndex(a => a.FamilyId);
        builder.HasIndex(a => a.ElderlyId);

        builder.OwnsMany(
            a => a.Answers,
            answer =>
            {
                answer.ToTable("care_assessment_answers");

                answer.WithOwner()
                    .HasForeignKey("AssessmentId");

                answer.Property<CareAssessmentId>("AssessmentId")
                    .HasConversion(
                        id => id.Value,
                        value => new CareAssessmentId(value))
                    .HasColumnName("assessment_id")
                    .IsRequired();

                answer.Property(ans => ans.Id)
                    .HasColumnName("id")
                    .ValueGeneratedNever();

                answer.HasKey("Id");

                answer.Property(ans => ans.QuestionId)
                    .HasConversion(
                        id => id.Value,
                        value => new AssessmentQuestionId(value))
                    .HasColumnName("question_id")
                    .IsRequired();

                answer.Property(ans => ans.SelectedOptionId)
                    .HasConversion(
                        id => id.Value,
                        value => new AssessmentOptionId(value))
                    .HasColumnName("selected_option_id")
                    .IsRequired();

                answer.Property(ans => ans.ScoreSnapshot)
                    .HasColumnName("score_snapshot")
                    .IsRequired();

                answer.HasIndex("AssessmentId");
            });

        builder.Ignore(a => a.DomainEvents);
    }
}