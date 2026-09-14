using Sanad.BuildingBlocks.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class UpdateLegalDocumentSectionsCommandHandler :
    ICommandHandler<
        UpdateLegalDocumentSectionsCommand,
        LegalDocumentResponse>
{
    private readonly ICmsDbContext _dbContext;

    public UpdateLegalDocumentSectionsCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LegalDocumentResponse>> Handle(
        UpdateLegalDocumentSectionsCommand request,
        CancellationToken cancellationToken)
    {
        LegalDocument? document =
            await _dbContext.LegalDocuments
                .SingleOrDefaultAsync(
                    item => item.Id == request.Id,
                    cancellationToken);

        if (document is null)
        {
            return LegalErrors.NotFound;
        }

        if (document.Status != LegalDocumentStatus.Draft)
        {
            return LegalErrors.PublishedDocumentImmutable;
        }

        IReadOnlyList<LegalSectionDraft> drafts =
            LegalSectionInput.ToDrafts(
                request.Sections ?? []);

        IReadOnlyList<string> shapeErrors =
            LegalDocumentShape.Validate(
                document.DocumentType,
                drafts);

        if (shapeErrors.Count > 0)
        {
            return LegalErrors.InvalidOperation;
        }

        try
        {
            document.UpdateSections(drafts);
        }
        catch (DomainException)
        {
            return LegalErrors.InvalidOperation;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return document.ToResponse();
    }
}
