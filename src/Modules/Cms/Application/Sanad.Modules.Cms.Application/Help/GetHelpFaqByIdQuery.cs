using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Application.Help;

public sealed record GetHelpFaqByIdQuery(
    HelpFaqId Id)
    : IQuery<HelpFaqResponse>;

public sealed class GetHelpFaqByIdQueryValidator
    : AbstractValidator<GetHelpFaqByIdQuery>
{
    public GetHelpFaqByIdQueryValidator()
    {
        RuleFor(query => query.Id).NotEqual(HelpFaqId.Empty);
    }
}

public sealed class GetHelpFaqByIdQueryHandler
    : IQueryHandler<GetHelpFaqByIdQuery, HelpFaqResponse>
{
    private readonly ICmsDbContext _dbContext;

    public GetHelpFaqByIdQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<HelpFaqResponse>> Handle(
        GetHelpFaqByIdQuery request,
        CancellationToken cancellationToken)
    {
        HelpFaq? faq =
            await _dbContext.HelpFaqs
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == request.Id,
                    cancellationToken);

        if (faq is null)
        {
            return HelpErrors.FaqNotFound;
        }

        return faq.ToResponse();
    }
}
