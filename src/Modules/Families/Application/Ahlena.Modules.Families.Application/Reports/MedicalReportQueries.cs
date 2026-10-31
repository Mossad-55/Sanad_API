using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.Reports;

namespace Ahlena.Modules.Families.Application.Reports;

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
