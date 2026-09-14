using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Help;

namespace Sanad.Modules.Cms.Application.Help;

public sealed class CreateHelpFaqCommandHandler :
    ICommandHandler<CreateHelpFaqCommand, HelpFaqResponse>
{
    private readonly ICmsDbContext _dbContext;

    public CreateHelpFaqCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<HelpFaqResponse>> Handle(
        CreateHelpFaqCommand request,
        CancellationToken cancellationToken)
    {
        HelpFaq faq = HelpFaq.Create(
            request.Audience,
            request.ArabicQuestion,
            request.EnglishQuestion,
            request.ArabicAnswer,
            request.EnglishAnswer,
            request.DisplayOrder,
            request.IsActive);

        _dbContext.HelpFaqs.Add(faq);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return faq.ToResponse();
    }
}
