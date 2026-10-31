using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Splash;

namespace Ahlena.Modules.Cms.Application.Splash;

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