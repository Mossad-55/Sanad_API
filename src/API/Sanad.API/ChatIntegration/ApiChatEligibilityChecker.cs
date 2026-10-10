using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Conversations;

namespace Sanad.API.ChatIntegration;

/// <summary>
/// Open-time eligibility for endpoint 2. Denies without ever explaining
/// itself: self-open, an unknown (or deleted) recipient, and an unusable
/// booking reference all collapse into the same privacy-preserving failure.
/// </summary>
public sealed class ApiChatEligibilityChecker : IChatEligibilityChecker
{
    private readonly IChatUserLookup _userLookup;

    public ApiChatEligibilityChecker(IChatUserLookup userLookup)
    {
        _userLookup = userLookup;
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

        // TODO(chat eligibility seam): verify booking state through a
        // CareHomes booking gateway — both users on the booking and
        // non-cancelled/non-expired per docs/chat/plan.md. Deliberately NOT
        // wired here: the Chat module must not take a cross-module project
        // reference and no booking-state port exists yet, so a booking id is
        // accepted as-is for now (booking-state check drops in behind this
        // seam without touching any handler).
        UserProfile? recipient = await _userLookup.GetProfileAsync(
            recipientUserId,
            cancellationToken);

        return recipient is not null;
    }
}
