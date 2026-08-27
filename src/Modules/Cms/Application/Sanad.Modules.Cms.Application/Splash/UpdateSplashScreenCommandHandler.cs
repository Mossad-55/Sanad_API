using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class UpdateSplashScreenCommandHandler :
    ICommandHandler<
        UpdateSplashScreenCommand,
        SplashScreenResponse>
{
    private readonly ICmsDbContext _dbContext;

    public UpdateSplashScreenCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SplashScreenResponse>> Handle(
        UpdateSplashScreenCommand request,
        CancellationToken cancellationToken)
    {
        SplashScreen? screen =
            await _dbContext.SplashScreens
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == request.Id,
                    cancellationToken);

        if (screen is null)
        {
            return SplashErrors.NotFound;
        }

        screen.UpdateContent(
            request.ArabicTitle,
            request.EnglishTitle,
            request.ArabicDescription,
            request.EnglishDescription,
            request.ArabicButtonText,
            request.EnglishButtonText,
            request.ImagePath,
            request.BackgroundColor,
            request.DisplayOrder);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return screen.ToResponse();
    }
}