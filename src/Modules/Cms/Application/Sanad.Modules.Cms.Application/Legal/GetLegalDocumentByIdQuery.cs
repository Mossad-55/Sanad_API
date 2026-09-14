using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Legal;

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
