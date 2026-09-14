using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class HelpFaqConfiguration :
    IEntityTypeConfiguration<HelpFaq>
{
    public void Configure(
        EntityTypeBuilder<HelpFaq> builder)
    {
        builder.ToTable("help_faqs");

        builder.HasKey(faq => faq.Id);

        builder.Property(faq => faq.Id)
            .HasConversion(
                id => id.Value,
                value => new HelpFaqId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(faq => faq.Audience)
            .HasColumnName("audience")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(faq => faq.ArabicQuestion)
            .HasColumnName("arabic_question")
            .HasMaxLength(HelpFaq.MaximumQuestionLength)
            .IsRequired();

        builder.Property(faq => faq.EnglishQuestion)
            .HasColumnName("english_question")
            .HasMaxLength(HelpFaq.MaximumQuestionLength)
            .IsRequired();

        builder.Property(faq => faq.ArabicAnswer)
            .HasColumnName("arabic_answer")
            .HasMaxLength(HelpFaq.MaximumAnswerLength)
            .IsRequired();

        builder.Property(faq => faq.EnglishAnswer)
            .HasColumnName("english_answer")
            .HasMaxLength(HelpFaq.MaximumAnswerLength)
            .IsRequired();

        builder.Property(faq => faq.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(faq => faq.IsActive)
            .HasColumnName("is_active")
            .IsRequired();

        builder.Property(faq => faq.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(faq => faq.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        // Active-FAQ reads per audience, ordered by display order.
        builder.HasIndex(faq => new
        {
            faq.Audience,
            faq.IsActive,
            faq.DisplayOrder
        });

        builder.Ignore(faq => faq.DomainEvents);
    }
}
