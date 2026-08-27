using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class PublishSplashScreenCommandHandler :
    ICommandHandler<
        PublishSplashScreenCommand,
        SplashScreenResponse>
{
    private readonly ICmsDbContext _dbContext;

    public PublishSplashScreenCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<SplashScreenResponse>> Handle(
        PublishSplashScreenCommand request,
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

        screen.Publish();

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return screen.ToResponse();
    }
}