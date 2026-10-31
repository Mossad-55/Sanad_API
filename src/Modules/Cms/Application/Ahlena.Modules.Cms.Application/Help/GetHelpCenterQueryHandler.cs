using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Application.Legal;
using Ahlena.Modules.Cms.Domain.Help;
using Ahlena.Modules.Cms.Domain.Legal;

namespace Ahlena.Modules.Cms.Application.Help;

public sealed class GetHelpCenterQueryHandler :
    IQueryHandler<GetHelpCenterQuery, HelpCenterResponse>
{
    private readonly ICmsDbContext _dbContext;

    public GetHelpCenterQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<HelpCenterResponse>> Handle(
        GetHelpCenterQuery request,
        CancellationToken cancellationToken)
    {
        if (!request.Audience.IsHelpAudience())
        {
            return ContentErrors.UnsupportedAudience;
        }

        List<HelpFaq> faqs =
            await _dbContext.HelpFaqs
                .AsNoTracking()
                .Where(faq =>
                    faq.Audience == request.Audience &&
                    faq.IsActive)
                .ToListAsync(cancellationToken);

        IReadOnlyList<HelpCenterFaqItem> items =
            faqs
                .OrderBy(faq => faq.DisplayOrder)
                .ThenBy(faq => faq.Id)
                .Select(faq => faq.ToAppItem())
                .ToList();

        SupportContact? contact =
            await _dbContext.SupportContacts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == SupportContact.SingletonId,
                    cancellationToken);

        HelpCenterSupportContact? supportContact =
            contact is null
                ? null
                : new HelpCenterSupportContact(
                    contact.SupportPhone,
                    contact.SupportEmail);

        return Result<HelpCenterResponse>.Success(
            new HelpCenterResponse(items, supportContact));
    }
}
