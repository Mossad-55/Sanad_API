using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class UnpublishSplashScreenCommandHandler :
    ICommandHandler<
        UnpublishSplashScreenCommand,
        SplashScreenResponse>
{
    private readonly ICmsDbContext _dbContext;

    public UnpublishSplashScreenCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SplashScreenResponse>> Handle(
        UnpublishSplashScreenCommand request,
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

        screen.Unpublish();

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return screen.ToResponse();
    }
}