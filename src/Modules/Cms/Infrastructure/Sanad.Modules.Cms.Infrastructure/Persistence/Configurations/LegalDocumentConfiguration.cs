using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Infrastructure.Persistence.Configurations;

public sealed class LegalDocumentConfiguration :
    IEntityTypeConfiguration<LegalDocument>
{
    public void Configure(
        EntityTypeBuilder<LegalDocument> builder)
    {
        builder.ToTable("legal_documents");

        builder.HasKey(document => document.Id);

        builder.Property(document => document.Id)
            .HasConversion(
                id => id.Value,
                value => new LegalDocumentId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(document => document.DocumentType)
            .HasColumnName("document_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(document => document.Audience)
            .HasColumnName("audience")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(document => document.Version)
            .HasColumnName("version")
            .IsRequired();

        builder.Property(document => document.Status)
            .HasColumnName("status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(document => document.CreatedOnUtc)
            .HasColumnName("created_on_utc")
            .IsRequired();

        builder.Property(document => document.UpdatedOnUtc)
            .HasColumnName("updated_on_utc")
            .IsRequired();

        builder.Property(document => document.PublishedOnUtc)
            .HasColumnName("published_on_utc");

        // One version number per (DocumentType, Audience).
        builder.HasIndex(document => new
        {
            document.DocumentType,
            document.Audience,
            document.Version
        })
        .IsUnique();

        // At most one Draft per (DocumentType, Audience) pair.
        builder.HasIndex(document => new
        {
            document.DocumentType,
            document.Audience,
            document.Status
        })
        .IsUnique()
        .HasFilter("status = 1");

        // Efficient current-published reads (status, type, audience, newest).
        builder.HasIndex(document => new
        {
            document.Status,
            document.DocumentType,
            document.Audience,
            document.Version
        });

        var stringListComparer = new ValueComparer<IReadOnlyList<string>>(
            (c1, c2) =>
                (c1 == null && c2 == null) ||
                (c1 != null && c2 != null && c1.SequenceEqual(c2)),
            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
            c => c.ToList().AsReadOnly());

        builder.OwnsMany(
            document => document.Sections,
            section =>
                LegalSectionConfiguration.Configure(
                    section,
                    stringListComparer));

        builder.Ignore(document => document.DomainEvents);
    }
}
