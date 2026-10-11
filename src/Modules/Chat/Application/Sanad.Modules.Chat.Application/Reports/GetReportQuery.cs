using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Reports;

namespace Sanad.Modules.Chat.Application.Reports;

/// <summary>
/// Endpoint 13 — one report's detail: the frozen report fields, status, and
/// resolution audit. This is a support-only read; there is deliberately no
/// participant gate (the ChatSupportAdmin policy is the authorization), and
/// an unknown id is a plain 404 through <see cref="ReportErrors.NotFound"/>.
/// </summary>
public sealed record GetReportQuery(ReportId ReportId)
    : IQuery<ReportResponse>;

public sealed class GetReportQueryHandler
    : IQueryHandler<GetReportQuery, ReportResponse>
{
    private readonly IChatDbContext _dbContext;

    public GetReportQueryHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ReportResponse>> Handle(
        GetReportQuery request,
        CancellationToken cancellationToken)
    {
        Report? report = await _dbContext.Reports
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.ReportId,
                cancellationToken);

        if (report is null)
        {
            return Result<ReportResponse>.Failure(
                ReportErrors.NotFound);
        }

        return Result<ReportResponse>.Success(
            ReportItemFactory.Create(report));
    }
}
