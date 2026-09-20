using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.Modules.Families.Application.Reports;

public sealed record VisitReportListItemResponse(
    Guid Id,
    Guid BookingId,
    Guid FamilyId,
    Guid ElderlyId,
    Guid CaregiverId,
    BookingCaregiverType CaregiverType,
    string? ObservedCondition,
    string? Activities,
    string? Notes,
    VisitReportAssessment Assessment,
    DateTime StartedOnUtc,
    DateTime CompletedOnUtc,
    DateTime SubmittedOnUtc);

public sealed record PagedVisitReportsResponse(
    IReadOnlyList<VisitReportListItemResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetFamilyVisitReportsQuery(
    UserId UserId,
    string? Type,
    ElderlyId? ElderlyId,
    BookingId? BookingId,
    int Page = 1,
    int PageSize = 50) : IQuery<PagedVisitReportsResponse>;

public sealed class GetFamilyVisitReportsQueryHandler
    : IQueryHandler<GetFamilyVisitReportsQuery, PagedVisitReportsResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetFamilyVisitReportsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedVisitReportsResponse>> Handle(
        GetFamilyVisitReportsQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.Type, "visit", StringComparison.OrdinalIgnoreCase))
        {
            return Result<PagedVisitReportsResponse>.Failure(VisitReportErrors.InvalidType);
        }

        int page = request.Page < 1 ? 1 : request.Page;
        int pageSize = request.PageSize is < 1 or > 50 ? 50 : request.PageSize;

        List<FamilyId> familyIds = await _dbContext.Families
            .AsNoTracking()
            .Where(family =>
                family.DeletedOnUtc == null &&
                (family.OwnerUserId == request.UserId ||
                 family.Members.Any(member => member.Id == request.UserId)))
            .Select(family => family.Id)
            .ToListAsync(cancellationToken);

        if (familyIds.Count == 0)
        {
            return Result<PagedVisitReportsResponse>.Failure(VisitReportErrors.AccessDenied);
        }

        IQueryable<VisitReport> query = _dbContext.VisitReports
            .AsNoTracking()
            .Where(report => familyIds.Contains(report.FamilyId));

        if (request.ElderlyId is not null)
            query = query.Where(report => report.ElderlyId == request.ElderlyId.Value);

        if (request.BookingId is not null)
            query = query.Where(report => report.BookingId == request.BookingId.Value);

        int totalCount = await query.CountAsync(cancellationToken);
        List<VisitReport> reports = await query
            .OrderByDescending(report => report.SubmittedOnUtc)
            .ThenByDescending(report => report.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        IReadOnlyList<VisitReportListItemResponse> items = reports
            .Select(report => new VisitReportListItemResponse(
                report.Id.Value,
                report.BookingId.Value,
                report.FamilyId.Value,
                report.ElderlyId.Value,
                report.CaregiverId.Value,
                report.CaregiverType,
                report.ObservedCondition,
                report.Activities,
                report.Notes,
                report.Assessment,
                report.StartedOnUtc,
                report.CompletedOnUtc,
                report.SubmittedOnUtc))
            .ToList();

        return Result<PagedVisitReportsResponse>.Success(
            new PagedVisitReportsResponse(items, page, pageSize, totalCount));
    }
}
