using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Cms.Application.Legal;

public static class LegalErrors
{
    public static readonly Error NotFound =
        new(
            "Cms.Legal.NotFound",
            "Legal document version was not found.");

    public static readonly Error NotPublished =
        new(
            "Cms.Legal.NotPublished",
            "No published legal document version exists for this audience.");

    public static readonly Error DraftAlreadyExists =
        new(
            "Cms.Legal.DraftAlreadyExists",
            "A draft already exists for this document type and audience.");

    public static readonly Error PublishedDocumentImmutable =
        new(
            "Cms.Legal.PublishedDocumentImmutable",
            "Published and archived legal document versions are immutable.");

    public static readonly Error InvalidOperation =
        new(
            "Cms.Legal.InvalidOperation",
            "The requested operation conflicts with the legal document rules.");
}
