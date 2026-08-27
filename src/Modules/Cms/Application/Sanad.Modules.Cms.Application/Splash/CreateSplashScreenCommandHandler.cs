using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class CreateSplashScreenCommandHandler :
    ICommandHandler<
        CreateSplashScreenCommand,
        SplashScreenResponse>
{
    private readonly ICmsDbContext _dbContext;

    public CreateSplashScreenCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SplashScreenResponse>> Handle(
        CreateSplashScreenCommand request,
        CancellationToken cancellationToken)
    {
        string internalName =
            request.InternalName.Trim();

        bool exists =
            await _dbContext.SplashScreens.AnyAsync(
                screen =>
                    screen.InternalName ==
                    internalName,
                cancellationToken);

        if (exists)
        {
            return SplashErrors
                .InternalNameAlreadyInUse;
        }

        SplashScreen screen =
            SplashScreen.Create(
                request.InternalName,
                request.ArabicTitle,
                request.EnglishTitle,
                request.ArabicDescription,
                request.EnglishDescription,
                request.ArabicButtonText,
                request.EnglishButtonText,
                request.ImagePath,
                request.BackgroundColor,
                request.DisplayOrder);

        _dbContext.SplashScreens.Add(
            screen);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return screen.ToResponse();
    }
}