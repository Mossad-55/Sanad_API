using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.CareRecords;

namespace Sanad.Modules.Families.Application.CareRecords;

// ==================== Admin operational list (read-only) ====================

/// <summary>
/// Locked D4 admin surface: SuperAdmin + SupportAdmin list/detail ONLY, with
/// a payload-free read audit row per call. Filters may narrow (source,
/// dependent, visit-date range, hospital search) but no filter grants
/// impersonation rights and there is no create/edit/delete anywhere on this
/// surface. Admin reads never return author editing capabilities.
/// </summary>
public sealed record ListAdminCareRecordsQuery(
    UserId AdminUserId,
    string? Source = null,
    Guid? DependentId = null,
    DateOnly? VisitDateFrom = null,
    DateOnly? VisitDateTo = null,
    string? Search = null,
    int Page = CareRecordPageSettings.DefaultPage,
    int PageSize = CareRecordPageSettings.DefaultPageSize,
    string CorrelationId = "") : IQuery<CareRecordsPage<CareRecordResponse>>;

public sealed class ListAdminCareRecordsQueryHandler
    : IQueryHandler<ListAdminCareRecordsQuery, CareRecordsPage<CareRecordResponse>>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public ListAdminCareRecordsQueryHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordsPage<CareRecordResponse>>> Handle(
        ListAdminCareRecordsQuery request,
        CancellationToken cancellationToken)
    {
        CareRecordSource? source = null;

        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            if (!Enum.TryParse(request.Source.Trim(), ignoreCase: true, out CareRecordSource parsed)
                || !Enum.IsDefined(parsed))
            {
                return CareRecordErrors.InvalidQuery;
            }

            source = parsed;
        }

        Error? filterError = CareRecordFilterValidation.Validate(
            source, request.VisitDateFrom, request.VisitDateTo);
        var pageQuery = new CareRecordPageQuery(request.Search, request.Page, request.PageSize);
        filterError ??= pageQuery.Normalize();

        if (filterError is not null)
        {
            return filterError;
        }

        IQueryable<CareRecord> query = _dbContext.CareRecords.AsNoTracking();

        if (request.DependentId.HasValue && request.DependentId.Value != Guid.Empty)
        {
            query = query.Where(r => r.ElderlyId == new ElderlyId(request.DependentId.Value));
        }

        query = query.Apply(source, request.VisitDateFrom, request.VisitDateTo, pageQuery.NormalizedSearch);

        int totalCount = await query.CountAsync(cancellationToken);

        List<CareRecordResponse> items = (await query
                .OrderNewestFirst()
                .Skip((pageQuery.Page - 1) * pageQuery.PageSize)
                .Take(pageQuery.PageSize)
                .ToListAsync(cancellationToken))
            .Select(record => record.ToResponse())
            .ToList();

        // Payload-free read audit: who listed care records and when — never
        // filter text or record content.
        _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAdminRead(
            recordId: null,
            elderlyId: null,
            request.AdminUserId,
            CareRecordAuditAction.AdminListed,
            request.CorrelationId,
            _dateTimeProvider.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return CareRecordPageQuery.CreatePage(items, pageQuery.Page, pageQuery.PageSize, totalCount);
    }
}

// ==================== Admin operational detail (read-only) ====================

public sealed record GetAdminCareRecordQuery(
    UserId AdminUserId,
    Guid RecordId,
    string CorrelationId = "") : IQuery<CareRecordResponse>;

public sealed class GetAdminCareRecordQueryHandler
    : IQueryHandler<GetAdminCareRecordQuery, CareRecordResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetAdminCareRecordQueryHandler(
        IFamiliesDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<CareRecordResponse>> Handle(
        GetAdminCareRecordQuery request,
        CancellationToken cancellationToken)
    {
        CareRecord? record = await _dbContext.CareRecords
            .AsNoTracking()
            .SingleOrDefaultAsync(r => r.Id == request.RecordId, cancellationToken);

        if (record is null)
        {
            return CareRecordErrors.NotFound;
        }

        _dbContext.CareRecordAudits.Add(CareRecordAudit.RecordAdminRead(
            record.Id,
            record.ElderlyId,
            request.AdminUserId,
            CareRecordAuditAction.AdminDetailRead,
            request.CorrelationId,
            _dateTimeProvider.UtcNow));
        await _dbContext.SaveChangesAsync(cancellationToken);

        return record.ToResponse();
    }
}
