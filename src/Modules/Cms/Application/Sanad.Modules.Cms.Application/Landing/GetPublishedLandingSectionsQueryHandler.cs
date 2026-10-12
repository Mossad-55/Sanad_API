using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Application.Landing;

public sealed class GetPublishedLandingSectionsQueryHandler :
    IQueryHandler<GetPublishedLandingSectionsQuery, IReadOnlyList<LandingSectionPublicItem>>
{
    private readonly ICmsDbContext _dbContext;

    public GetPublishedLandingSectionsQueryHandler(ICmsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<LandingSectionPublicItem>>> Handle(
        GetPublishedLandingSectionsQuery request,
        CancellationToken cancellationToken)
    {
        List<LandingSection> sections = await _dbContext.LandingSections
            .AsNoTracking()
            .Where(section =>
                section.Status == LandingSectionStatus.Published &&
                section.IsVisible &&
                section.Type != LandingSectionType.Stats)
            .OrderBy(section => section.DisplayOrder)
            .ThenBy(section => section.CreatedOnUtc)
            .ThenBy(section => section.Id)
            .ToListAsync(cancellationToken);

        IReadOnlyList<LandingSectionPublicItem> items = sections
            .Select(section => section.ToPublicItem())
            .ToList();

        return Result<IReadOnlyList<LandingSectionPublicItem>>.Success(items);
    }
}
