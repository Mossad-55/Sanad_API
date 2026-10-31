using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.CareRecords;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.Modules.Families.Application.CareRecords;

// ==================== Elderly self timeline (read-only) ====================

/// <summary>
/// The elderly user's own combined timeline across all three sources. Read
/// only — there is no create/edit/delete path for the elderly portal — and
/// strictly self-scoped: the dependent is derived from the authenticated
/// user's IdentityUserId; this route never accepts an alternate elderly id.
/// </summary>
public sealed record ListElderlyCareRecordsQuery(
    UserId ElderlyUserId,
    CareRecordSource? Source = null,
    DateOnly? VisitDateFrom = null,
    DateOnly? VisitDateTo = null,
    string? Search = null,
    int Page = CareRecordPageSettings.DefaultPage,
    int PageSize = CareRecordPageSettings.DefaultPageSize) : IQuery<CareRecordsPage<CareRecordResponse>>;

public sealed class ListElderlyCareRecordsQueryHandler
    : IQueryHandler<ListElderlyCareRecordsQuery, CareRecordsPage<CareRecordResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListElderlyCareRecordsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CareRecordsPage<CareRecordResponse>>> Handle(
        ListElderlyCareRecordsQuery request,
        CancellationToken cancellationToken)
    {
        Error? filterError = CareRecordFilterValidation.Validate(
            request.Source, request.VisitDateFrom, request.VisitDateTo);
        var pageQuery = new CareRecordPageQuery(request.Search, request.Page, request.PageSize);
        filterError ??= pageQuery.Normalize();

        if (filterError is not null)
        {
            return filterError;
        }

        Elderly? elderly = await _dbContext.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(e => e.IdentityUserId == request.ElderlyUserId, cancellationToken);

        if (elderly is null)
        {
            return CareRecordErrors.AccessDenied;
        }

        IQueryable<CareRecord> query = _dbContext.CareRecords
            .AsNoTracking()
            .Where(r => r.ElderlyId == elderly.Id);

        query = query.Apply(
            request.Source,
            request.VisitDateFrom,
            request.VisitDateTo,
            pageQuery.NormalizedSearch);

        int totalCount = await query.CountAsync(cancellationToken);

        List<CareRecordResponse> items = (await query
                .OrderNewestFirst()
                .Skip((pageQuery.Page - 1) * pageQuery.PageSize)
                .Take(pageQuery.PageSize)
                .ToListAsync(cancellationToken))
            .Select(record => record.ToResponse())
            .ToList();

        return CareRecordPageQuery.CreatePage(items, pageQuery.Page, pageQuery.PageSize, totalCount);
    }
}
