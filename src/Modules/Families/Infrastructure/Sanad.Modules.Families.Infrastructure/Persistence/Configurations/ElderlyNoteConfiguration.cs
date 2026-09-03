using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Notes;

namespace Sanad.Modules.Families.Infrastructure.Persistence.Configurations;

public sealed class ElderlyNoteConfiguration : IEntityTypeConfiguration<ElderlyNote>
{
    public void Configure(EntityTypeBuilder<ElderlyNote> builder)
    {
        builder.ToTable("elderly_notes");

        builder.HasKey(n => n.Id);

        builder.Property(n => n.Id)
            .HasConversion(id => id.Value, value => new ElderlyNoteId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(n => n.ElderlyId)
            .HasConversion(id => id.Value, value => new ElderlyId(value))
            .HasColumnName("elderly_id")
            .IsRequired();

        builder.Property(n => n.AuthorUserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("author_user_id")
            .IsRequired();

        builder.Property(n => n.Title)
            .HasMaxLength(ElderlyNote.MaximumTitleLength)
            .HasColumnName("title")
            .IsRequired();

        builder.Property(n => n.Description)
            .HasMaxLength(ElderlyNote.MaximumDescriptionLength)
            .HasColumnName("description")
            .IsRequired();

        builder.Property(n => n.Category)
            .HasConversion<int>()
            .HasColumnName("category")
            .IsRequired();

        builder.Property(n => n.Priority)
            .HasConversion<int>()
            .HasColumnName("priority")
            .IsRequired();

        builder.Property(n => n.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(n => n.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.HasIndex(n => n.ElderlyId);
        builder.HasIndex(n => n.CreatedOnUtc);

        builder.Ignore(n => n.DomainEvents);
    }
}