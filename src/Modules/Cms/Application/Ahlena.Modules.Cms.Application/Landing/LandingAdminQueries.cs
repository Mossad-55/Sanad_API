using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Landing;
using Ahlena.Modules.Families.Application.Abstractions.Data;

namespace Ahlena.Modules.Cms.Application.Landing;

public sealed record GetLandingAdminListQuery(
    LandingSectionStatus? Status = null,
    LandingSectionType? Type = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20)
    : IQuery<LandingAdminPageResponse>;

public sealed class GetLandingAdminListQueryHandler(
    ICmsDbContext db,
    IFamiliesDbContext familiesDb,
    ICaregiversDbContext caregiversDb)
    : IQueryHandler<GetLandingAdminListQuery, LandingAdminPageResponse>
{
    public async Task<Result<LandingAdminPageResponse>> Handle(
        GetLandingAdminListQuery request, CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        IQueryable<LandingSection> query = db.LandingSections.AsNoTracking();

        if (request.Status is LandingSectionStatus status)
            query = query.Where(section => section.Status == status);

        if (request.Type is LandingSectionType type)
            query = query.Where(section => section.Type == type);

        string? term = string.IsNullOrWhiteSpace(request.Search)
            ? null
            : request.Search.Trim().ToLower();
        if (term is not null)
        {
            query = query.Where(section =>
                section.ArabicTitle.ToLower().Contains(term) ||
                section.EnglishTitle.ToLower().Contains(term));
        }

        int totalCount = await query.CountAsync(cancellationToken);

        List<LandingSection> sections = await query
            .OrderBy(section => section.DisplayOrder)
            .ThenBy(section => section.CreatedOnUtc)
            .ThenBy(section => section.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        Dictionary<Guid, ResolvedSectionRefs> resolved = await LandingRefResolver.ResolveAsync(
            db, familiesDb, caregiversDb, sections, cancellationToken);

        return Result<LandingAdminPageResponse>.Success(
            new LandingAdminPageResponse(
                sections
                    .Select(section =>
                    {
                        resolved.TryGetValue(section.Id.Value, out ResolvedSectionRefs? refs);
                        return section.ToAdminResponse(refs?.DanglingRefCount ?? 0);
                    })
                    .ToList(),
                page,
                pageSize,
                totalCount));
    }
}
