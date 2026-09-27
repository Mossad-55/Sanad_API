using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.NotificationsIntegration;

public sealed class FamilyNotificationRecipientGateway(IFamiliesDbContext families, ISender sender)
    : INotificationRecipientGateway
{
    public async Task<IReadOnlyList<UserId>> GetCheckInAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default)
    {
        var familyId = await families.Elderlies.AsNoTracking()
            .Where(x => x.IdentityUserId == elderly.ElderlyIdentityUserId && x.Id.Value == elderly.ElderlyEntityId)
            .Select(x => (Guid?)x.FamilyId.Value).SingleOrDefaultAsync(cancellationToken);
        if (familyId is null) return [];

        var userIds = await families.Families.AsNoTracking()
            .Where(x => x.Id.Value == familyId.Value && x.DeletedOnUtc == null)
            .SelectMany(x => x.Members.Select(member => member.Id))
            .Distinct().ToListAsync(cancellationToken);

        var result = await sender.Send(new GetActiveUsersWithCheckInAlertsQuery(userIds), cancellationToken);
        return result.IsSuccess ? result.Value.Distinct().ToArray() : [];
    }
}
