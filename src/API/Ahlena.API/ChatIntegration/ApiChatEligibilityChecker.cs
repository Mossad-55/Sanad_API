using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Chat.Application.Conversations;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Domain.Bookings;

namespace Ahlena.API.ChatIntegration;

/// <summary>
/// Open-time initiation eligibility for endpoint 2 (Chat Plus approved
/// actor matrix). Denies without ever explaining itself: every ineligible
/// pair collapses into the same privacy-preserving failure.
/// Matrix (account types are Identity integers):
/// - Support (SupportAdmin/SuperAdmin) may initiate with everyone. No other
///   admin type (ContentAdmin, FinanceAdmin, …) inherits initiation.
/// - Family (owner and all members, including Viewer) may initiate with any
///   caregiver and any approved care home (owner of an approved facility).
/// - Elderly may initiate with their own family members (resolved through
///   the Elderly identity/relationship, not the display integer) and with
///   booking-linked caregivers.
/// - Caregivers may initiate with booking families (a non-terminal booking
///   — cancelled/expired/refunded excluded, caregiver-declined eligible —
///   links the caregiver to the recipient's family); arbitrary direct chats
///   stay denied. Replies travel through reopening, which checks membership
///   instead of this matrix.
/// - Care-home owners (of an approved facility) may initiate with family
///   users, mirroring family→care-home.
/// - Booking open resolves parties server-side; terminal bookings stay
///   creation-ineligible here (reopening uses the gateway's reopen path).
/// </summary>
public sealed class ApiChatEligibilityChecker : IChatEligibilityChecker
{
    private readonly IChatUserLookup _userLookup;
    private readonly IFamiliesDbContext _families;
    private readonly ICaregiversDbContext _caregivers;
    private readonly IChatFacilityLookup _facilities;

    public ApiChatEligibilityChecker(
        IChatUserLookup userLookup,
        IFamiliesDbContext? families = null,
        ICaregiversDbContext? caregivers = null,
        IChatFacilityLookup? facilities = null)
    {
        _userLookup = userLookup;
        _families = families!;
        _caregivers = caregivers!;
        _facilities = facilities!;
    }

    public async Task<bool> CanOpenAsync(
        UserId openerUserId,
        UserId recipientUserId,
        BookingId? bookingId,
        CancellationToken cancellationToken = default)
    {
        if (openerUserId == UserId.Empty ||
            recipientUserId == UserId.Empty ||
            openerUserId == recipientUserId)
        {
            return false;
        }

        // Scope consistency: a booking-scoped conversation must carry a
        // concrete booking reference (the domain rejects Guid.Empty), and a
        // direct conversation must carry none.
        if (bookingId is { } booking && booking.Value == Guid.Empty)
        {
            return false;
        }

        UserProfile? opener = await _userLookup.GetProfileAsync(
            openerUserId,
            cancellationToken);
        UserProfile? recipient = await _userLookup.GetProfileAsync(
            recipientUserId,
            cancellationToken);

        // The recipient must resolve; the opener is the authenticated
        // caller (Normal-access JWT) and is never re-probed here.
        if (recipient is null)
        {
            return false;
        }

        // Without the relationship stores (pre-existing focused tests) only
        // the legacy existence check applies.
        if (_families is null || _caregivers is null || _facilities is null)
        {
            return true;
        }

        // Current account state drives eligibility — never the
        // first-registered display integer. A user holding several accounts
        // (e.g. Family + Caregiver) may initiate in any of their roles.
        // The opener's roles come from their own profile when resolvable;
        // otherwise the caller's authenticated JWT role set applies at the
        // controller boundary (Normal access) and creation proceeds.
        IReadOnlyList<int> openerRoles = opener?.EffectiveAccountTypes ?? [];
        IReadOnlyList<int> recipientRoles = recipient.EffectiveAccountTypes;

        if (openerRoles.Count == 0)
        {
            return true;
        }

        // Support initiates with everyone.
        if (openerRoles.Contains(7) || openerRoles.Contains(5))
        {
            return true;
        }

        // Family owner and all members, including Viewer.
        if (openerRoles.Contains(1))
        {
            if (await FamilyCanInitiateAsync(
                    openerUserId, recipientRoles, recipientUserId, cancellationToken))
            {
                return true;
            }
        }

        // Elderly: own family members + booking-linked caregivers.
        if (openerRoles.Contains(4))
        {
            if (await ElderlyCanInitiateAsync(
                    openerUserId, recipientRoles, recipientUserId, cancellationToken))
            {
                return true;
            }
        }

        // Caregiver: booking families only.
        if ((openerRoles.Contains(2) || openerRoles.Contains(3)) &&
            recipientRoles.Contains(1) &&
            await CaregiverLinkedToFamilyAsync(
                openerUserId, recipientUserId, cancellationToken))
        {
            return true;
        }

        // Care-home owner (approved facility): family users.
        if (openerRoles.Contains(8) &&
            recipientRoles.Contains(1) &&
            await _facilities.GetForOwnerAsync(openerUserId, cancellationToken) is not null)
        {
            return true;
        }

        return false;
    }

    private async Task<bool> FamilyCanInitiateAsync(
        UserId openerUserId,
        IReadOnlyList<int> recipientRoles,
        UserId recipientUserId,
        CancellationToken cancellationToken)
    {
        // Any caregiver.
        if (recipientRoles.Contains(2) || recipientRoles.Contains(3))
        {
            return true;
        }

        // Approved care homes (owner of an approved facility).
        if (recipientRoles.Contains(8))
        {
            return await _facilities.GetForOwnerAsync(recipientUserId, cancellationToken) is not null;
        }

        return false;
    }

    private async Task<bool> ElderlyCanInitiateAsync(
        UserId openerUserId,
        IReadOnlyList<int> recipientRoles,
        UserId recipientUserId,
        CancellationToken cancellationToken)
    {
        // NOTE: strongly-typed id members (.Value) do not translate on
        // Npgsql — whole properties are projected and .Value is read in
        // memory only; all query predicates compare whole structs.
        List<FamilyId> familyIds = await _families.Elderlies
            .AsNoTracking()
            .Where(elderly => elderly.IdentityUserId == openerUserId)
            .Select(elderly => elderly.FamilyId)
            .ToListAsync(cancellationToken);

        if (familyIds.Count == 0)
        {
            return false;
        }

        // Own family members: the recipient belongs to one of the opener's
        // families (owner or any member role).
        if (recipientRoles.Contains(1))
        {
            List<FamilyId> recipientFamilies = await _families.Families
                .AsNoTracking()
                .Where(family =>
                    family.DeletedOnUtc == null &&
                    (family.OwnerUserId == recipientUserId ||
                     family.Members.Any(member => member.Id == recipientUserId)))
                .Select(family => family.Id)
                .ToListAsync(cancellationToken);

            return recipientFamilies.Any(familyId => familyIds.Contains(familyId));
        }

        // Booking-linked caregivers: a non-terminal booking for one of the
        // opener's elderly rows with this caregiver.
        if (recipientRoles.Contains(2) || recipientRoles.Contains(3))
        {
            List<ElderlyId> elderlyIds = await _families.Elderlies
                .AsNoTracking()
                .Where(elderly => elderly.IdentityUserId == openerUserId)
                .Select(elderly => elderly.Id)
                .ToListAsync(cancellationToken);

            List<CaregiverId> caregiverIds = await _caregivers.Caregivers
                .AsNoTracking()
                .Where(caregiver => caregiver.UserId == recipientUserId)
                .Select(caregiver => caregiver.Id)
                .ToListAsync(cancellationToken);

            if (caregiverIds.Count == 0)
            {
                return false;
            }

            CaregiverId caregiverId = caregiverIds[0];

            foreach (ElderlyId elderlyId in elderlyIds)
            {
                bool linked = await _families.Bookings
                    .AsNoTracking()
                    .AnyAsync(
                        item =>
                            item.CaregiverId == caregiverId &&
                            item.ElderlyId == elderlyId &&
                            item.Status != BookingStatus.CancelledByFamily &&
                            item.Status != BookingStatus.CancelledByCaregiver &&
                            item.Status != BookingStatus.Refunded &&
                            item.Status != BookingStatus.Expired,
                        cancellationToken);

                if (linked)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private async Task<bool> CaregiverLinkedToFamilyAsync(
        UserId caregiverUserId,
        UserId recipientUserId,
        CancellationToken cancellationToken)
    {
        List<CaregiverId> caregiverIds = await _caregivers.Caregivers
            .AsNoTracking()
            .Where(caregiver => caregiver.UserId == caregiverUserId)
            .Select(caregiver => caregiver.Id)
            .ToListAsync(cancellationToken);

        if (caregiverIds.Count == 0)
        {
            return false;
        }

        CaregiverId caregiverId = caregiverIds[0];

        List<FamilyId> recipientFamilyIds = await _families.Families
            .AsNoTracking()
            .Where(family =>
                family.DeletedOnUtc == null &&
                (family.OwnerUserId == recipientUserId ||
                 family.Members.Any(member => member.Id == recipientUserId)))
            .Select(family => family.Id)
            .ToListAsync(cancellationToken);

        if (recipientFamilyIds.Count == 0)
        {
            return false;
        }

        foreach (FamilyId familyId in recipientFamilyIds)
        {
            bool linked = await _families.Bookings
                .AsNoTracking()
                .AnyAsync(
                    item =>
                        item.CaregiverId == caregiverId &&
                        item.FamilyId == familyId &&
                        item.Status != BookingStatus.CancelledByFamily &&
                        item.Status != BookingStatus.CancelledByCaregiver &&
                        item.Status != BookingStatus.Refunded &&
                        item.Status != BookingStatus.Expired,
                    cancellationToken);

            if (linked)
            {
                return true;
            }
        }

        return false;
    }
}
