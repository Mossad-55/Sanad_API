using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Splash;

namespace Ahlena.Modules.Cms.Application.Splash;

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