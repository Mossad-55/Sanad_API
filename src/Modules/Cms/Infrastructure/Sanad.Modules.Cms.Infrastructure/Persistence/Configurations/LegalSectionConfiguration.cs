using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

/// <summary>
/// Relational mapping for the legal_sections table. LegalSection is an owned
/// entity type, so EF requires its mapping to run inside the owner's
/// OwnsMany configuration (see <see cref="LegalDocumentConfiguration"/>);
/// this class keeps the section columns, bullet JSON conversion, and the
/// per-document unique display order in their own file.
/// </summary>
public sealed class LegalSectionConfiguration
{
    public static void Configure(
        OwnedNavigationBuilder<LegalDocument, LegalSection> builder,
        ValueComparer<IReadOnlyList<string>> stringListComparer)
    {
        builder.ToTable("legal_sections");

        builder.WithOwner()
            .HasForeignKey("DocumentId");

        builder.Property<LegalDocumentId>("DocumentId")
            .HasConversion(
                id => id.Value,
                value => new LegalDocumentId(value))
            .HasColumnName("document_id")
            .IsRequired();

        builder.Property(section => section.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.HasKey("Id");

        builder.Property(section => section.SectionType)
            .HasColumnName("section_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(section => section.DisplayOrder)
            .HasColumnName("display_order")
            .IsRequired();

        builder.Property(section => section.ArabicTitle)
            .HasColumnName("arabic_title")
            .HasMaxLength(LegalSection.MaximumTitleLength)
            .IsRequired();

        builder.Property(section => section.EnglishTitle)
            .HasColumnName("english_title")
            .HasMaxLength(LegalSection.MaximumTitleLength)
            .IsRequired();

        builder.Property(section => section.ArabicDescription)
            .HasColumnName("arabic_description")
            .HasMaxLength(LegalSection.MaximumDescriptionLength)
            .IsRequired();

        builder.Property(section => section.EnglishDescription)
            .HasColumnName("english_description")
            .HasMaxLength(LegalSection.MaximumDescriptionLength)
            .IsRequired();

        builder.Property(section => section.ArabicBullets)
            .HasConversion(
                v => JsonSerializer.Serialize(
                    v,
                    (JsonSerializerOptions?)null),
                v => JsonSerializer
                    .Deserialize<List<string>>(
                        v,
                        (JsonSerializerOptions?)null)
                    ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(section => section.ArabicBullets)
            .HasColumnName("arabic_bullets")
            .IsRequired();

        builder.Property(section => section.EnglishBullets)
            .HasConversion(
                v => JsonSerializer.Serialize(
                    v,
                    (JsonSerializerOptions?)null),
                v => JsonSerializer
                    .Deserialize<List<string>>(
                        v,
                        (JsonSerializerOptions?)null)
                    ?? new List<string>())
            .Metadata.SetValueComparer(stringListComparer);

        builder.Property(section => section.EnglishBullets)
            .HasColumnName("english_bullets")
            .IsRequired();

        // Unique positive display order per document version.
        builder.HasIndex("DocumentId", "DisplayOrder")
            .IsUnique();
    }
}
