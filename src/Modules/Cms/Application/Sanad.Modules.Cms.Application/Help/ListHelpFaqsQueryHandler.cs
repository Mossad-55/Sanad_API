using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Application.Help;

public sealed class ListHelpFaqsQueryHandler :
    IQueryHandler<
        ListHelpFaqsQuery,
        IReadOnlyList<HelpFaqResponse>>
{
    private readonly ICmsDbContext _dbContext;

    public ListHelpFaqsQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<HelpFaqResponse>>> Handle(
        ListHelpFaqsQuery request,
        CancellationToken cancellationToken)
    {
        List<HelpFaq> faqs =
            await _dbContext.HelpFaqs
                .AsNoTracking()
                .Where(faq =>
                    (request.Audience is null ||
                        faq.Audience == request.Audience) &&
                    (request.IsActive is null ||
                        faq.IsActive == request.IsActive))
                .ToListAsync(cancellationToken);

        IReadOnlyList<HelpFaqResponse> response =
            faqs
                .OrderBy(faq => faq.Audience)
                .ThenBy(faq => faq.DisplayOrder)
                .ThenBy(faq => faq.Id.Value)
                .Select(faq => faq.ToResponse())
                .ToList();

        return Result<IReadOnlyList<HelpFaqResponse>>.Success(response);
    }
}
