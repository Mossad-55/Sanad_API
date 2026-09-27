using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Notifications.Application.Abstractions.Recipients;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.API.NotificationsIntegration;

public sealed class FamilyNotificationRecipientGateway(IFamiliesDbContext families, ISender sender, ICaregiversDbContext caregivers)
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
    public async Task<IReadOnlyList<UserId>> GetHelpRequestAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default)
    {
        var familyId = await families.Elderlies.AsNoTracking().Where(x => x.IdentityUserId == elderly.ElderlyIdentityUserId && x.Id.Value == elderly.ElderlyEntityId).Select(x => (Guid?)x.FamilyId.Value).SingleOrDefaultAsync(cancellationToken);
        if (familyId is null) return [];
        var ids = await families.Families.AsNoTracking().Where(x => x.Id.Value == familyId && x.DeletedOnUtc == null).SelectMany(x => x.Members.Select(m => m.Id)).Distinct().ToListAsync(cancellationToken);
        var result = await sender.Send(new GetActiveUsersWithHelpRequestAlertsQuery(ids), cancellationToken);
        return result.IsSuccess ? result.Value : [];
    }

    public async Task<IReadOnlyList<UserId>> GetMedicationAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default)
    {
        var profile = await families.Elderlies.AsNoTracking()
            .Where(x => x.IdentityUserId == elderly.ElderlyIdentityUserId && x.Id.Value == elderly.ElderlyEntityId)
            .Select(x => new { x.FamilyId }).SingleOrDefaultAsync(cancellationToken);
        if (profile is null) return [];
        var familyIds = await families.Families.AsNoTracking()
            .Where(x => x.Id == profile.FamilyId && x.DeletedOnUtc == null)
            .SelectMany(x => x.Members.Select(m => m.Id)).Distinct().ToListAsync(cancellationToken);
        var familyRecipients = await sender.Send(new GetActiveUsersWithMedicationRemindersQuery(familyIds), cancellationToken);
        var caregiverIds = await families.Bookings.AsNoTracking()
            .Where(x => x.ElderlyId.Value == elderly.ElderlyEntityId && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.InProgress))
            .Select(x => x.CaregiverId.Value).Distinct().ToListAsync(cancellationToken);
        var caregiverUsers = await caregivers.Caregivers.AsNoTracking()
            .Where(x => caregiverIds.Contains(x.Id.Value)).Select(x => x.UserId).ToListAsync(cancellationToken);
        var caregiverRecipients = await sender.Send(new GetActiveUsersWithMedicationRemindersQuery(caregiverUsers), cancellationToken);
        var admins = await sender.Send(new GetActiveSupportAdminUserIdsQuery(), cancellationToken);
        return familyRecipients.Value.Concat(caregiverRecipients.Value).Concat(admins.Value).Distinct().ToArray();
    }

    public async Task<IReadOnlyList<UserId>> GetSosAlertRecipientsAsync(ElderlyRecipient elderly, CancellationToken cancellationToken = default)
    {
        var profile = await families.Elderlies.AsNoTracking()
            .Where(x => x.IdentityUserId == elderly.ElderlyIdentityUserId && x.Id.Value == elderly.ElderlyEntityId)
            .Select(x => new { x.FamilyId }).SingleOrDefaultAsync(cancellationToken);
        if (profile is null) return [];
        var familyIds = await families.Families.AsNoTracking()
            .Where(x => x.Id == profile.FamilyId && x.DeletedOnUtc == null)
            .SelectMany(x => x.Members.Select(m => m.Id)).Distinct().ToListAsync(cancellationToken);
        var familyRecipients = await sender.Send(new GetActiveUsersWithHelpRequestAlertsQuery(familyIds), cancellationToken);
        var caregiverIds = await families.Bookings.AsNoTracking()
            .Where(x => x.ElderlyId.Value == elderly.ElderlyEntityId && (x.Status == BookingStatus.Confirmed || x.Status == BookingStatus.InProgress))
            .Select(x => x.CaregiverId.Value).Distinct().ToListAsync(cancellationToken);
        var caregiverUsers = await caregivers.Caregivers.AsNoTracking()
            .Where(x => caregiverIds.Contains(x.Id.Value)).Select(x => x.UserId).ToListAsync(cancellationToken);
        var caregiverRecipients = await sender.Send(new GetActiveUsersWithHelpRequestAlertsQuery(caregiverUsers), cancellationToken);
        var admins = await sender.Send(new GetActiveSupportAdminUserIdsQuery(), cancellationToken);
        return familyRecipients.Value.Concat(caregiverRecipients.Value).Concat(admins.Value).Distinct().ToArray();
    }
}
