using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;

namespace Sanad.Modules.Cms.Application.Help;

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
        if (!request.Audience.IsDefined())
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
                .ThenBy(faq => faq.Id.Value)
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
