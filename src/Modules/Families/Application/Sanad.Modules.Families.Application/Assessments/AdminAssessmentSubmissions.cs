using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;

namespace Sanad.Modules.Families.Application.Assessments;

public sealed record AdminAssessmentSubmissionItem(
    CareAssessmentId Id,
    FamilyId FamilyId,
    ElderlyId? ElderlyId,
    AssessmentTierId TierId,
    int TotalScore,
    DateTime CompletedOnUtc);

public sealed record PagedAssessmentSubmissions(
    IReadOnlyList<AdminAssessmentSubmissionItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record ListAdminAssessmentSubmissionsQuery(
    int Page,
    int PageSize,
    FamilyId? FamilyId = null,
    AssessmentTierId? TierId = null)
    : IQuery<PagedAssessmentSubmissions>;

public sealed class ListAdminAssessmentSubmissionsQueryHandler
    : IQueryHandler<ListAdminAssessmentSubmissionsQuery, PagedAssessmentSubmissions>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListAdminAssessmentSubmissionsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedAssessmentSubmissions>> Handle(
        ListAdminAssessmentSubmissionsQuery request,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.CareAssessments.AsNoTracking();

        if (request.FamilyId.HasValue && request.FamilyId.Value != FamilyId.Empty)
        {
            query = query.Where(a => a.FamilyId == request.FamilyId.Value);
        }

        if (request.TierId.HasValue && request.TierId.Value != AssessmentTierId.Empty)
        {
            query = query.Where(a => a.AssessmentTierId == request.TierId.Value);
        }

        int totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(a => a.CompletedOnUtc)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(a => new AdminAssessmentSubmissionItem(
                a.Id,
                a.FamilyId,
                a.ElderlyId,
                a.AssessmentTierId,
                a.TotalScore,
                a.CompletedOnUtc))
            .ToListAsync(cancellationToken);

        return new PagedAssessmentSubmissions(
            items,
            request.Page,
            request.PageSize,
            totalCount);
    }
}