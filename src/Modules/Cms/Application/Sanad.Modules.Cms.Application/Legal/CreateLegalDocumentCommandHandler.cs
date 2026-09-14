using Sanad.BuildingBlocks.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class CreateLegalDocumentCommandHandler :
    ICommandHandler<
        CreateLegalDocumentCommand,
        LegalDocumentResponse>
{
    private readonly ICmsDbContext _dbContext;

    public CreateLegalDocumentCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LegalDocumentResponse>> Handle(
        CreateLegalDocumentCommand request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.DocumentType) ||
            !request.Audience.IsDefined())
        {
            return LegalErrors.InvalidOperation;
        }

        IReadOnlyList<LegalSectionDraft> drafts =
            LegalSectionInput.ToDrafts(
                request.Sections ?? []);

        IReadOnlyList<string> shapeErrors =
            LegalDocumentShape.Validate(
                request.DocumentType,
                drafts);

        if (shapeErrors.Count > 0)
        {
            return LegalErrors.InvalidOperation;
        }

        bool draftExists =
            await _dbContext.LegalDocuments.AnyAsync(
                document =>
                    document.DocumentType == request.DocumentType &&
                    document.Audience == request.Audience &&
                    document.Status == LegalDocumentStatus.Draft,
                cancellationToken);

        if (draftExists)
        {
            return LegalErrors.DraftAlreadyExists;
        }

        List<int> existingVersions =
            await _dbContext.LegalDocuments
                .Where(document =>
                    document.DocumentType == request.DocumentType &&
                    document.Audience == request.Audience)
                .Select(document => document.Version)
                .ToListAsync(cancellationToken);

        int nextVersion =
            existingVersions.Count == 0
                ? 1
                : existingVersions.Max() + 1;

        LegalDocument document;

        try
        {
            document = LegalDocument.Create(
                request.DocumentType,
                request.Audience,
                nextVersion,
                drafts);
        }
        catch (DomainException)
        {
            return LegalErrors.InvalidOperation;
        }

        _dbContext.LegalDocuments.Add(document);

        await _dbContext.SaveChangesAsync(cancellationToken);

        return document.ToResponse();
    }
}
