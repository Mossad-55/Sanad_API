using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Infrastructure.Persistence.Configurations;

/// <summary>
/// E07 append-only caregiver review notes. The table is insert-only: no update
/// or delete path exists in this phase, so a note's text, author and creation
/// instant are retained for the life of the caregiver row. The index matches the
/// pinned read — one caregiver's notes newest-first.
/// </summary>
public sealed class CaregiverReviewNoteConfiguration
    : IEntityTypeConfiguration<CaregiverReviewNote>
{
    public void Configure(EntityTypeBuilder<CaregiverReviewNote> builder)
    {
        builder.ToTable("caregiver_review_notes");

        builder.HasKey(note => note.Id);
        builder.Property(note => note.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(note => note.CaregiverId)
            .HasConversion(id => id.Value, value => new CaregiverId(value))
            .HasColumnName("caregiver_id")
            .IsRequired();
        builder.Property(note => note.AuthorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("author_user_id")
            .IsRequired();
        builder.Property(note => note.Text)
            .HasColumnName("text")
            .HasMaxLength(CaregiverReviewNote.MaximumTextLength)
            .IsRequired();
        builder.Property(note => note.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.HasOne<Caregiver>()
            .WithMany()
            .HasForeignKey(note => note.CaregiverId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(note => new { note.CaregiverId, note.CreatedOnUtc });
    }
}
