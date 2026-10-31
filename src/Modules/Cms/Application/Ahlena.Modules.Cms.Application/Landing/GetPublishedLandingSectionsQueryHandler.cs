using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Landing;
using Ahlena.Modules.Families.Application.Abstractions.Data;

namespace Ahlena.Modules.Cms.Application.Landing;

public sealed class GetPublishedLandingSectionsQueryHandler :
    IQueryHandler<GetPublishedLandingSectionsQuery, IReadOnlyList<LandingSectionPublicItem>>
{
    private readonly ICmsDbContext _dbContext;
    private readonly IFamiliesDbContext _familiesDb;
    private readonly ICaregiversDbContext _caregiversDb;

    public GetPublishedLandingSectionsQueryHandler(
        ICmsDbContext dbContext,
        IFamiliesDbContext familiesDb,
        ICaregiversDbContext caregiversDb)
    {
        _dbContext = dbContext;
        _familiesDb = familiesDb;
        _caregiversDb = caregiversDb;
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

        Dictionary<Guid, ResolvedSectionRefs> resolved = await LandingRefResolver.ResolveAsync(
            _dbContext, _familiesDb, _caregiversDb, sections, cancellationToken);

        IReadOnlyList<LandingSectionPublicItem> items = sections
            .Select(section =>
            {
                resolved.TryGetValue(section.Id.Value, out ResolvedSectionRefs? refs);
                return section.ToPublicItem(refs?.Testimonials, refs?.Faqs);
            })
            .ToList();

        return Result<IReadOnlyList<LandingSectionPublicItem>>.Success(items);
    }
}
