using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record GetActiveUsersWithCheckInAlertsQuery(IReadOnlyCollection<UserId> UserIds)
    : IQuery<IReadOnlyList<UserId>>;

public sealed class GetActiveUsersWithCheckInAlertsQueryHandler(IIdentityDbContext db)
    : IQueryHandler<GetActiveUsersWithCheckInAlertsQuery, IReadOnlyList<UserId>>
{
    public async Task<Result<IReadOnlyList<UserId>>> Handle(GetActiveUsersWithCheckInAlertsQuery request, CancellationToken cancellationToken)
    {
        var ids = request.UserIds.Distinct().ToArray();
        if (ids.Length == 0) return Array.Empty<UserId>();
        var result = await db.Users.AsNoTracking()
            .Where(x => ids.Contains(x.Id) && x.Status == UserStatus.Active && x.NotificationPreferences.CheckInAlerts)
            .Select(x => x.Id).ToListAsync(cancellationToken);
        return result;
    }
}

public sealed record GetActiveUsersWithHelpRequestAlertsQuery(IReadOnlyCollection<UserId> UserIds) : IQuery<IReadOnlyList<UserId>>;
public sealed class GetActiveUsersWithHelpRequestAlertsQueryHandler(IIdentityDbContext db) : IQueryHandler<GetActiveUsersWithHelpRequestAlertsQuery, IReadOnlyList<UserId>>
{ public async Task<Result<IReadOnlyList<UserId>>> Handle(GetActiveUsersWithHelpRequestAlertsQuery r, CancellationToken ct) => await db.Users.AsNoTracking().Where(x => r.UserIds.Contains(x.Id) && x.Status == UserStatus.Active && x.NotificationPreferences.HelpRequestAlerts).Select(x => x.Id).ToListAsync(ct); }
