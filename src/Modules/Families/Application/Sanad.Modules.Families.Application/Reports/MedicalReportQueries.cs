using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Reports;

namespace Sanad.Modules.Families.Application.Reports;

public sealed record GetMedicalReportPhotoQuery(UserId UserId, MedicalReportId ReportId, bool Caregiver) : IQuery<MedicalReportPhotoResult>;
public sealed record MedicalReportPhotoResult(string Key);

public sealed class GetMedicalReportPhotoQueryHandler : IQueryHandler<GetMedicalReportPhotoQuery, MedicalReportPhotoResult>
{
    private readonly IFamiliesDbContext _db;
    public GetMedicalReportPhotoQueryHandler(IFamiliesDbContext db) { _db = db; }
    public async Task<Result<MedicalReportPhotoResult>> Handle(GetMedicalReportPhotoQuery request, CancellationToken cancellationToken)
    {
        MedicalReport? report = await _db.MedicalReports.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.ReportId, cancellationToken);
        if (report?.PhotoKey is null) return Result<MedicalReportPhotoResult>.Failure(MedicalReportErrors.PhotoNotFound);
        if (request.Caregiver)
        {
            if (report.CaregiverUserId != request.UserId) return Result<MedicalReportPhotoResult>.Failure(MedicalReportErrors.AccessDenied);
        }
        else
        {
            var family = await FamilyAccess.ResolveFamilyAsync(_db, request.UserId, cancellationToken);
            if (family is null || family.Id != report.FamilyId || !FamilyAccess.IsMember(family, request.UserId)) return Result<MedicalReportPhotoResult>.Failure(MedicalReportErrors.AccessDenied);
        }
        return Result<MedicalReportPhotoResult>.Success(new(report.PhotoKey));
    }
}
