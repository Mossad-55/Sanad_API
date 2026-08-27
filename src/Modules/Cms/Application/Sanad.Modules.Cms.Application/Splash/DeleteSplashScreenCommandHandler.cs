using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class DeleteSplashScreenCommandHandler :
    ICommandHandler<
        DeleteSplashScreenCommand>
{
    private readonly ICmsDbContext _dbContext;

    public DeleteSplashScreenCommandHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(DeleteSplashScreenCommand request, CancellationToken cancellationToken)
    {
        SplashScreen? screen =
            await _dbContext.SplashScreens
                .SingleOrDefaultAsync(
                    item =>
                        item.Id == request.Id,
                    cancellationToken);

        if (screen is null)
        {
            return Result.Failure(
                SplashErrors.NotFound);
        }

        _dbContext.SplashScreens.Remove(
            screen);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}