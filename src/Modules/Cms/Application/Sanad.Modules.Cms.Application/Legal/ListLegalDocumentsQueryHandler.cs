using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

public sealed class ListLegalDocumentsQueryHandler :
    IQueryHandler<
        ListLegalDocumentsQuery,
        IReadOnlyList<LegalDocumentResponse>>
{
    private readonly ICmsDbContext _dbContext;

    public ListLegalDocumentsQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<LegalDocumentResponse>>> Handle(
        ListLegalDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        List<LegalDocument> documents =
            await _dbContext.LegalDocuments
                .AsNoTracking()
                .Where(document =>
                    (request.DocumentType is null ||
                        document.DocumentType == request.DocumentType) &&
                    (request.Audience is null ||
                        document.Audience == request.Audience) &&
                    (request.Status is null ||
                        document.Status == request.Status))
                .ToListAsync(cancellationToken);

        // Newest version first within each (type, audience) pair.
        IReadOnlyList<LegalDocumentResponse> response =
            documents
                .OrderBy(document => document.DocumentType)
                .ThenBy(document => document.Audience)
                .ThenByDescending(document => document.Version)
                .Select(document => document.ToResponse())
                .ToList();

        return Result<IReadOnlyList<LegalDocumentResponse>>.Success(response);
    }
}
