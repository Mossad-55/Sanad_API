using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed class GetPublishedSplashScreensQueryHandler :
    IQueryHandler<
        GetPublishedSplashScreensQuery,
        IReadOnlyList<SplashScreenPublicItem>>
{
    private readonly ICmsDbContext _dbContext;

    public GetPublishedSplashScreensQueryHandler(
        ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<SplashScreenPublicItem>>> Handle(
        GetPublishedSplashScreensQuery request,
        CancellationToken cancellationToken)
    {
        List<SplashScreen> screens =
            await _dbContext.SplashScreens
                .AsNoTracking()
                .Where(screen =>
                    screen.Status ==
                        SplashPublicationStatus.Published)
                .OrderBy(screen =>
                    screen.DisplayOrder)
                .ToListAsync(
                    cancellationToken);

        IReadOnlyList<SplashScreenPublicItem> items =
            screens
                .Select(screen =>
                    screen.ToPublicItem())
                .ToList();

        return Result<IReadOnlyList<SplashScreenPublicItem>>
            .Success(items);
    }
}