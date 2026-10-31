using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Help;

namespace Ahlena.Modules.Cms.Application.Help;

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
                    (request.Audience == null ||
                        faq.Audience == request.Audience) &&
                    (request.IsActive == null ||
                        faq.IsActive == request.IsActive))
                .ToListAsync(cancellationToken);

        IReadOnlyList<HelpFaqResponse> response =
            faqs
                .OrderBy(faq => faq.Audience)
                .ThenBy(faq => faq.DisplayOrder)
                .ThenBy(faq => faq.Id)
                .Select(faq => faq.ToResponse())
                .ToList();

        return Result<IReadOnlyList<HelpFaqResponse>>.Success(response);
    }
}
