using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Legal;

public sealed record GetLegalDocumentByIdQuery(
    LegalDocumentId Id)
    : IQuery<LegalDocumentResponse>;

public sealed class GetLegalDocumentByIdQueryValidator
    : AbstractValidator<GetLegalDocumentByIdQuery>
{
    public GetLegalDocumentByIdQueryValidator()
    {
        RuleFor(query => query.Id).NotEqual(LegalDocumentId.Empty);
    }
}

public sealed class GetLegalDocumentByIdQueryHandler
    : IQueryHandler<GetLegalDocumentByIdQuery, LegalDocumentResponse>
{
    private readonly ICmsDbContext _dbContext;

    public GetLegalDocumentByIdQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<LegalDocumentResponse>> Handle(
        GetLegalDocumentByIdQuery request,
        CancellationToken cancellationToken)
    {
        LegalDocument? document =
            await _dbContext.LegalDocuments
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == request.Id,
                    cancellationToken);

        if (document is null)
        {
            return LegalErrors.NotFound;
        }

        return document.ToResponse();
    }
}
