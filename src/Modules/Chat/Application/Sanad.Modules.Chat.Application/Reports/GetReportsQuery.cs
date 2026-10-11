using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.Modules.Chat.Application.Reports;

/// <summary>
/// Endpoint 12 — the support report queue: optionally filtered by
/// <c>Status</c> (the wire values "Open"/"Dismissed"/"Upheld", matched
/// case-insensitively; null/blank means no filter), newest report first, paged
/// with the same envelope and clamps as every other Chat list. An
/// unrecognised status value is the caller's own 400
/// <see cref="ReportErrors.Invalid"/> — silently answering with an unfiltered
/// page would hide a typo.
/// </summary>
public sealed record GetReportsQuery(
    string? Status,
    int Page,
    int PageSize)
    : IQuery<PagedResult<ReportResponse>>;

public sealed class GetReportsQueryHandler
    : IQueryHandler<GetReportsQuery, PagedResult<ReportResponse>>
{
    private const int MaxPageSize = 100;

    private readonly IChatDbContext _dbContext;

    public GetReportsQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedResult<ReportResponse>>> Handle(
        GetReportsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        if (!TryParseStatus(request.Status, out ReportStatus? status))
        {
            return Result<PagedResult<ReportResponse>>.Failure(
                ReportErrors.Invalid);
        }

        IQueryable<Report> scoped = _dbContext.Reports
            .AsNoTracking();

        if (status is { } filter)
        {
            scoped = scoped.Where(report => report.Status == filter);
        }

        int totalCount = await scoped.CountAsync(cancellationToken);

        List<Report> rows = await scoped
            .OrderByDescending(report => report.CreatedOnUtc)
            .ThenByDescending(report => report.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = new List<ReportResponse>(rows.Count);

        foreach (Report report in rows)
        {
            items.Add(ReportItemFactory.Create(report));
        }

        return Result<PagedResult<ReportResponse>>.Success(
            new PagedResult<ReportResponse>(
                items,
                page,
                pageSize,
                totalCount,
                (int)Math.Ceiling(totalCount / (double)pageSize)));
    }

    /// <summary>Blank means "no filter"; anything else must name one of the
    /// three lifecycle states. Numeric strings are accepted only when they
    /// resolve to a defined <see cref="ReportStatus"/>.</summary>
    private static bool TryParseStatus(
        string? value,
        out ReportStatus? status)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            status = null;
            return true;
        }

        if (Enum.TryParse(
                value.Trim(),
                ignoreCase: true,
                out ReportStatus parsed) &&
            Enum.IsDefined(parsed))
        {
            status = parsed;
            return true;
        }

        status = null;
        return false;
    }
}
