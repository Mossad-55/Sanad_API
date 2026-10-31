using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Help;

namespace Ahlena.Modules.Cms.Application.Help;

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
