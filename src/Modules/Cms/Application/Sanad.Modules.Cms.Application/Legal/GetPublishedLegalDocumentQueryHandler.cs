using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class GetPublishedLegalDocumentQueryHandler :
    IQueryHandler<
        GetPublishedLegalDocumentQuery,
        LegalDocumentResponse>
{
    private readonly ICmsDbContext _dbContext;

    public GetPublishedLegalDocumentQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LegalDocumentResponse>> Handle(
        GetPublishedLegalDocumentQuery request,
        CancellationToken cancellationToken)
    {
        if (!Enum.IsDefined(request.DocumentType) ||
            !request.Audience.IsDefined())
        {
            return ContentErrors.UnsupportedAudience;
        }

        // The unique (type, audience, version) index guarantees at most one
        // Published row per pair; the ordering keeps the read deterministic.
        List<LegalDocument> published =
            await _dbContext.LegalDocuments
                .AsNoTracking()
                .Where(item =>
                    item.DocumentType == request.DocumentType &&
                    item.Audience == request.Audience &&
                    item.Status == LegalDocumentStatus.Published)
                .ToListAsync(cancellationToken);

        LegalDocument? document = published
            .OrderByDescending(item => item.Version)
            .FirstOrDefault();

        if (document is null)
        {
            return LegalErrors.NotPublished;
        }

        return document.ToResponse();
    }
}
