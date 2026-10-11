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

        // Booking validity is NOT checked here: booking-only opens are
        // resolved (and validated against the booking stores) by
        // ApiChatBookingGateway before this gate runs, and a direct open
        // carries no booking by the exactly-one-target rule. The scope
        // consistency above still applies to whatever arrives.
        UserProfile? recipient = await _userLookup.GetProfileAsync(
            recipientUserId,
            cancellationToken);

        return recipient is not null;
    }
}
