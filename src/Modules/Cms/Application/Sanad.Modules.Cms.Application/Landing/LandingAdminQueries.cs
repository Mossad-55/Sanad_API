using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Application.Landing;

public sealed record GetLandingAdminListQuery(
    LandingSectionStatus? Status = null,
    LandingSectionType? Type = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 20)
    : IQuery<LandingAdminPageResponse>;

public sealed class GetLandingAdminListQueryHandler(ICmsDbContext db)
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

        return Result<LandingAdminPageResponse>.Success(
            new LandingAdminPageResponse(
                sections.Select(section => section.ToAdminResponse()).ToList(),
                page,
                pageSize,
                totalCount));
    }
}
