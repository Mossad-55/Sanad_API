using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Domain.Reports;

namespace Ahlena.Modules.Chat.Application.Reports;

/// <summary>
/// Endpoint 14 — Support closes a report without action (204). Nothing is
/// erased: the reason, the reporter, and the reported conversation stay
/// readable through endpoints 12–13, and the resolution stamps the acting
/// admin's JWT user id plus the time. Only an Open report resolves; any
/// other attempt is the 409 <see cref="ReportErrors.InvalidState"/>.
/// </summary>
public sealed record DismissReportCommand(
    UserId Admin,
    ReportId ReportId)
    : ICommand;

public sealed class DismissReportCommandHandler
    : ICommandHandler<DismissReportCommand>
{
    private readonly IChatDbContext _dbContext;

    public DismissReportCommandHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        DismissReportCommand request,
        CancellationToken cancellationToken)
    {
        // The admin's own token must carry a usable actor id; without one
        // the resolution audit would be meaningless (400, never a 500).
        if (request.Admin == UserId.Empty)
        {
            return Result.Failure(ReportErrors.Invalid);
        }

        Report? report = await _dbContext.Reports
            .SingleOrDefaultAsync(
                item => item.Id == request.ReportId,
                cancellationToken);

        if (report is null)
        {
            return Result.Failure(ReportErrors.NotFound);
        }

        DateTime resolvedOnUtc = DateTime.UtcNow;

        try
        {
            report.Dismiss(request.Admin, resolvedOnUtc);
        }
        catch (DomainException)
        {
            // Preconditions above pin admin and UTC stamp, so the only
            // DomainException the Open-only guard can throw is a resolve
            // that already happened: dismiss-after-dismiss, uphold-after-
            // dismiss, or the reverse.
            return Result.Failure(ReportErrors.InvalidState);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // A concurrent resolve won the version token first: exactly one
            // conditional Open-to-Dismissed transition wins, the loser is
            // the 409. Stale tracking is cleared so the scoped context is
            // never reused with a half-resolved aggregate.
            if (_dbContext is DbContext context)
            {
                context.ChangeTracker.Clear();
            }

            return Result.Failure(ReportErrors.InvalidState);
        }

        return Result.Success();
    }
}
