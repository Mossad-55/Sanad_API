using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Open-time booking seam for endpoint 2 (Opus delta). Implemented in
/// Sanad.API over the Families/CareHomes booking stores — the Chat
/// Application declares only the seam so it never takes a cross-module
/// project reference. Resolves the counterpart of a booking the opener is
/// allowed to chat about, or null when the booking is unknown, the opener
/// is not a party to it, or it is cancelled/expired/refunded. A null must
/// surface as the privacy-preserving 404
/// (<see cref="ConversationErrors.NotFound"/>) and never reveal which check
/// failed.
/// </summary>
public interface IChatBookingGateway
{
    Task<UserId?> ResolveCounterpartAsync(
        BookingId bookingId,
        UserId openerUserId,
        CancellationToken cancellationToken = default);
}
