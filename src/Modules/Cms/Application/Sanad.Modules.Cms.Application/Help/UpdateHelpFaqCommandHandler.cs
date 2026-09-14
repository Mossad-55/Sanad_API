using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Application.Help;

public sealed class UpdateHelpFaqCommandHandler :
    ICommandHandler<UpdateHelpFaqCommand, HelpFaqResponse>
{
    private readonly ICmsDbContext _dbContext;

    public UpdateHelpFaqCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<HelpFaqResponse>> Handle(
        UpdateHelpFaqCommand request,
        CancellationToken cancellationToken)
    {
        HelpFaq? faq =
            await _dbContext.HelpFaqs
                .SingleOrDefaultAsync(
                    item => item.Id == request.Id,
                    cancellationToken);

        if (faq is null)
        {
            return HelpErrors.FaqNotFound;
        }

        faq.UpdateContent(
            request.Audience,
            request.ArabicQuestion,
            request.EnglishQuestion,
            request.ArabicAnswer,
            request.EnglishAnswer,
            request.DisplayOrder,
            request.IsActive);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return faq.ToResponse();
    }
}
