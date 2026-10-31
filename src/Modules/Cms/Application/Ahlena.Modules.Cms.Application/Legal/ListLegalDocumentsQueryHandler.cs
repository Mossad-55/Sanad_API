using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Legal;

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
                    (request.DocumentType == null ||
                        document.DocumentType == request.DocumentType) &&
                    (request.Audience == null ||
                        document.Audience == request.Audience) &&
                    (request.Status == null ||
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
