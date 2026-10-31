using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Help;

namespace Ahlena.Modules.Cms.Application.Help;

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
