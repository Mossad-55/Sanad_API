using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.Modules.Families.Application.Reports;

public sealed record FamilyReportListItemResponse(Guid Id, Guid BookingId, Guid FamilyId, Guid ElderlyId, Guid CaregiverId, string ReportType, BookingCaregiverType CaregiverType, int? Systolic, int? Diastolic, int? Pulse, decimal? Temperature, string? Notes, DateTime? MeasurementTakenOnUtc, VisitReportAssessment Assessment, string? ObservedCondition, string? Activities, DateTime StartedOnUtc, DateTime CompletedOnUtc, DateTime SubmittedOnUtc, bool PhotoAvailable);
public sealed record PagedFamilyReportsResponse(IReadOnlyList<FamilyReportListItemResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record GetFamilyReportsQuery(UserId UserId, string? Type, ElderlyId? ElderlyId, BookingId? BookingId, int Page = 1, int PageSize = 50) : IQuery<PagedFamilyReportsResponse>;

public sealed class GetFamilyReportsQueryHandler : IQueryHandler<GetFamilyReportsQuery, PagedFamilyReportsResponse>
{
    private readonly IFamiliesDbContext _db;
    public GetFamilyReportsQueryHandler(IFamiliesDbContext db) { _db = db; }
    public async Task<Result<PagedFamilyReportsResponse>> Handle(GetFamilyReportsQuery request, CancellationToken cancellationToken)
    {
        if (request.Type is not null && !new[] { "visit", "medical", "all" }.Contains(request.Type, StringComparer.OrdinalIgnoreCase)) return Result<PagedFamilyReportsResponse>.Failure(new Error("Reports.InvalidType", "Report type is invalid."));
        var families = await _db.Families.AsNoTracking().Where(x => x.DeletedOnUtc == null && (x.OwnerUserId == request.UserId || x.Members.Any(m => m.Id == request.UserId))).Select(x => x.Id).ToListAsync(cancellationToken);
        if (families.Count == 0) return Result<PagedFamilyReportsResponse>.Failure(new Error("Reports.AccessDenied", "The current user has no active family membership."));
        int page = request.Page < 1 ? 1 : request.Page, size = request.PageSize is < 1 or > 50 ? 50 : request.PageSize;
        List<VisitReport> visits = string.Equals(request.Type, "medical", StringComparison.OrdinalIgnoreCase) ? [] : await _db.VisitReports.AsNoTracking().Where(x => families.Contains(x.FamilyId) && (request.ElderlyId == null || x.ElderlyId == request.ElderlyId) && (request.BookingId == null || x.BookingId == request.BookingId)).ToListAsync(cancellationToken);
        List<MedicalReport> medical = string.Equals(request.Type, "visit", StringComparison.OrdinalIgnoreCase) ? [] : await _db.MedicalReports.AsNoTracking().Where(x => families.Contains(x.FamilyId) && (request.ElderlyId == null || x.ElderlyId == request.ElderlyId) && (request.BookingId == null || x.BookingId == request.BookingId)).ToListAsync(cancellationToken);
        var items = visits.Select(x => new FamilyReportListItemResponse(x.Id.Value, x.BookingId.Value, x.FamilyId.Value, x.ElderlyId.Value, x.CaregiverId.Value, "visit", x.CaregiverType, null, null, null, null, x.Notes, null, x.Assessment, x.ObservedCondition, x.Activities, default, default, x.SubmittedOnUtc, false)).Concat(medical.Select(x => new FamilyReportListItemResponse(x.Id.Value, x.BookingId.Value, x.FamilyId.Value, x.ElderlyId.Value, x.CaregiverId.Value, "medical", x.CaregiverType, x.Systolic, x.Diastolic, x.Pulse, x.Temperature, x.Notes, x.MeasurementTakenOnUtc, x.Assessment, null, null, default, default, x.SubmittedOnUtc, x.PhotoKey is not null))).OrderByDescending(x => x.SubmittedOnUtc).ThenByDescending(x => x.Id).ToList();
        int total = items.Count;
        return Result<PagedFamilyReportsResponse>.Success(new(items.Skip((page - 1) * size).Take(size).ToList(), page, size, total));
    }
}
