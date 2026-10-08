using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Application.Discovery;

namespace Sanad.Modules.CareHomes.Application.Facilities;

public sealed record CareHomeLicenseExpiryItem(
    Guid FacilityId,
    CareHomeStatus FacilityStatus,
    string ArabicName,
    string EnglishName,
    Guid LicenseDocumentId,
    DateOnly ExpiryDate,
    int DaysUntilExpiry,
    bool IsExpired,
    DateTime? VerifiedOnUtc);

public sealed record CareHomeLicenseExpiryPage(
    DateOnly ThroughDate,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CareHomeLicenseExpiryItem> Items);

public sealed record GetAdminCareHomeLicenseExpiriesQuery(
    DateOnly ThroughDate,
    int Page,
    int PageSize,
    DateTime UtcNow) : IQuery<CareHomeLicenseExpiryPage>;

public sealed class GetAdminCareHomeLicenseExpiriesQueryHandler(ICareHomesDbContext db)
    : IQueryHandler<GetAdminCareHomeLicenseExpiriesQuery, CareHomeLicenseExpiryPage>
{
    public async Task<Result<CareHomeLicenseExpiryPage>> Handle(
        GetAdminCareHomeLicenseExpiriesQuery request, CancellationToken ct)
    {
        if (request.ThroughDate == default || request.Page < 1 || request.PageSize is < 1 or > 100
            || request.UtcNow.Kind != DateTimeKind.Utc)
            return Result<CareHomeLicenseExpiryPage>.Failure(CareHomeAdminErrors.InvalidQuery);

        DateOnly cairoToday = CareHomePublicEligibility.CairoDate(request.UtcNow);
        var licenses = db.Facilities.AsNoTracking()
            .Where(facility => facility.Status == CareHomeStatus.Approved && facility.ApprovedRevisionId != null)
            .Select(facility => new
            {
                Facility = facility,
                Revision = facility.Revisions.FirstOrDefault(revision =>
                    revision.Id == facility.ApprovedRevisionId && revision.ApprovedOnUtc != null),
                License = facility.Documents
                    .Where(document => document.ProfileRevisionId == facility.ApprovedRevisionId
                        && document.Type == CareHomeDocumentType.OperatingLicense)
                    .OrderByDescending(document => document.CreatedOnUtc)
                    .ThenByDescending(document => document.Id)
                    .FirstOrDefault()
            })
            .Where(item => item.Revision != null && item.License != null
                && item.License.Status == CareHomeDocumentStatus.Verified
                && !item.License.VerifiedNonExpiring
                && item.License.ExpiryDate != null
                && item.License.ExpiryDate <= request.ThroughDate);

        int count = await licenses.CountAsync(ct);
        var page = await licenses
            .OrderBy(item => item.License!.ExpiryDate)
            .ThenBy(item => item.Facility.Id)
            .Skip((request.Page - 1) * request.PageSize)
            .Take(request.PageSize)
            .Select(item => new
            {
                item.Facility.Id.Value,
                item.Facility.Status,
                item.Revision!.ArabicName,
                item.Revision.EnglishName,
                LicenseDocumentId = item.License!.Id,
                ExpiryDate = item.License.ExpiryDate!.Value,
                item.License.VerifiedOnUtc
            })
            .ToListAsync(ct);
        CareHomeLicenseExpiryItem[] items = page.Select(item => new CareHomeLicenseExpiryItem(
            item.Value, item.Status, item.ArabicName, item.EnglishName, item.LicenseDocumentId,
            item.ExpiryDate, item.ExpiryDate.DayNumber - cairoToday.DayNumber,
            item.ExpiryDate < cairoToday, item.VerifiedOnUtc)).ToArray();

        return Result<CareHomeLicenseExpiryPage>.Success(new(
            request.ThroughDate, request.Page, request.PageSize, count, items));
    }
}
