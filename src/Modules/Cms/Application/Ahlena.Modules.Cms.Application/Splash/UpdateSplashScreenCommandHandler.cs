using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Splash;

namespace Ahlena.Modules.Cms.Application.Splash;

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

        string imagePath =
            string.IsNullOrWhiteSpace(request.ImagePath)
                ? screen.ImagePath
                : request.ImagePath;

        screen.UpdateContent(
            request.ArabicTitle,
            request.EnglishTitle,
            request.ArabicDescription,
            request.EnglishDescription,
            request.ArabicButtonText,
            request.EnglishButtonText,
            imagePath,
            request.BackgroundColor,
            request.DisplayOrder);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return screen.ToResponse();
    }
}