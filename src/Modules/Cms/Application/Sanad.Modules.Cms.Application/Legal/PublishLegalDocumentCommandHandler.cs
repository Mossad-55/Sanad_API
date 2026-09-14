using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class PublishLegalDocumentCommandHandler :
    ICommandHandler<
        PublishLegalDocumentCommand,
        LegalDocumentResponse>
{
    private readonly ICmsDbContext _dbContext;

    public PublishLegalDocumentCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LegalDocumentResponse>> Handle(
        PublishLegalDocumentCommand request,
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

        // Publishing an already published version is an idempotent success.
        if (document.Status == LegalDocumentStatus.Published)
        {
            return document.ToResponse();
        }

        if (document.Status != LegalDocumentStatus.Draft)
        {
            return LegalErrors.InvalidOperation;
        }

        // The previous current version of the same pair is archived in the
        // same SaveChanges transaction, so exactly one Published version
        // remains. History is retained; nothing is deleted.
        List<LegalDocument> currentPublished =
            await _dbContext.LegalDocuments
                .Where(item =>
                    item.DocumentType == document.DocumentType &&
                    item.Audience == document.Audience &&
                    item.Status == LegalDocumentStatus.Published &&
                    item.Id != document.Id)
                .ToListAsync(cancellationToken);

        foreach (LegalDocument previous in currentPublished)
        {
            previous.Archive();
        }

        document.Publish();

        await _dbContext.SaveChangesAsync(cancellationToken);

        return document.ToResponse();
    }
}
