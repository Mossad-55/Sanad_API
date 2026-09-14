using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.Modules.Cms.Domain.Legal;

/// <summary>
/// One immutable-by-default version of one legal document for one audience.
/// Version numbers are server assigned per (DocumentType, Audience) pair and
/// start at 1. All versions are retained: Draft -> Published -> Archived.
/// </summary>
public sealed class LegalDocument : AggregateRoot<LegalDocumentId>
{
    public const int MaximumSectionCount = 40;

    private readonly List<LegalSection> _sections = [];

    private LegalDocument()
    {
    }

    private LegalDocument(
        LegalDocumentId id,
        LegalDocumentType documentType,
        LegalAudience audience,
        int version,
        DateTime createdOnUtc)
        : base(id)
    {
        DocumentType = documentType;
        Audience = audience;
        Version = version;
        Status = LegalDocumentStatus.Draft;
        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public LegalDocumentType DocumentType { get; private set; }
    public LegalAudience Audience { get; private set; }
    public int Version { get; private set; }
    public LegalDocumentStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? PublishedOnUtc { get; private set; }

    public IReadOnlyList<LegalSection> Sections => _sections;

    public static LegalDocument Create(
        LegalDocumentType documentType,
        LegalAudience audience,
        int version,
        IReadOnlyList<LegalSectionDraft> sections)
    {
        if (!Enum.IsDefined(documentType))
        {
            throw new DomainException(
                "Document type is not a supported value.");
        }

        if (!audience.IsDefined())
        {
            throw new DomainException(
                "Audience is not a supported app audience.");
        }

        if (version < 1)
        {
            throw new DomainException(
                "Version must be a positive number.");
        }

        EnsureShapeIsValid(documentType, sections);

        LegalDocument document = new(
            LegalDocumentId.New(),
            documentType,
            audience,
            version,
            DateTime.UtcNow);

        document.ReplaceSections(sections);

        return document;
    }

    /// <summary>
    /// Replaces the section list of a Draft. Published and Archived versions
    /// are immutable.
    /// </summary>
    public void UpdateSections(
        IReadOnlyList<LegalSectionDraft> sections)
    {
        EnsureDraft();

        EnsureShapeIsValid(DocumentType, sections);

        ReplaceSections(sections);

        UpdatedOnUtc = DateTime.UtcNow;
    }

    /// <summary>
    /// Draft -> Published. Already published documents are a no-op.
    /// </summary>
    public void Publish()
    {
        if (Status == LegalDocumentStatus.Published)
        {
            return;
        }

        if (Status != LegalDocumentStatus.Draft)
        {
            throw new DomainException(
                "Only a draft legal document can be published.");
        }

        DateTime nowUtc = DateTime.UtcNow;

        Status = LegalDocumentStatus.Published;
        PublishedOnUtc = nowUtc;
        UpdatedOnUtc = nowUtc;
    }

    /// <summary>
    /// Published -> Archived. Called for the previous current version when a
    /// new draft is published; archived rows stay queryable forever.
    /// </summary>
    public void Archive()
    {
        if (Status == LegalDocumentStatus.Archived)
        {
            return;
        }

        if (Status != LegalDocumentStatus.Published)
        {
            throw new DomainException(
                "Only a published legal document can be archived.");
        }

        Status = LegalDocumentStatus.Archived;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    private void EnsureDraft()
    {
        if (Status != LegalDocumentStatus.Draft)
        {
            throw new DomainException(
                "Only a draft legal document can be modified.");
        }
    }

    private static void EnsureShapeIsValid(
        LegalDocumentType documentType,
        IReadOnlyList<LegalSectionDraft> sections)
    {
        IReadOnlyList<string> errors =
            LegalDocumentShape.Validate(
                documentType,
                sections);

        if (errors.Count > 0)
        {
            throw new DomainException(
                string.Join(" ", errors));
        }
    }

    private void ReplaceSections(
        IReadOnlyList<LegalSectionDraft> sections)
    {
        _sections.Clear();

        foreach (LegalSectionDraft section in sections)
        {
            _sections.Add(
                LegalSection.Create(
                    section.SectionType,
                    section.DisplayOrder,
                    section.ArabicTitle,
                    section.EnglishTitle,
                    section.ArabicDescription,
                    section.EnglishDescription,
                    section.ArabicBullets,
                    section.EnglishBullets));
        }
    }
}
