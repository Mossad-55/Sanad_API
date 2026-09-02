using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;

namespace Sanad.Modules.Cms.Application.Splash;

public sealed record GetAllSplashScreensQuery
    : IQuery<IReadOnlyList<SplashScreenResponse>>;

public sealed class GetAllSplashScreensQueryHandler
    : IQueryHandler<GetAllSplashScreensQuery, IReadOnlyList<SplashScreenResponse>>
{
    private readonly ICmsDbContext _dbContext;

    public GetAllSplashScreensQueryHandler(ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<SplashScreenResponse>>> Handle(
        GetAllSplashScreensQuery request,
        CancellationToken cancellationToken)
    {
        var screens = await _dbContext.SplashScreens
            .AsNoTracking()
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        IReadOnlyList<SplashScreenResponse> response =
            screens.Select(s => s.ToResponse()).ToList();

        return Result<IReadOnlyList<SplashScreenResponse>>.Success(response);
    }
}