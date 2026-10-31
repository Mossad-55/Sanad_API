using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Application.Conversations;

/// <summary>
/// Open-time booking seam for endpoint 2 (Opus delta). Implemented in
/// Ahlena.API over the Families/CareHomes booking stores — the Chat
/// Application declares only the seam so it never takes a cross-module
/// project reference. Resolves the counterpart of a booking the opener is
/// allowed to chat about, or null when the booking is unknown, the opener
/// is not a party to it, or it is cancelled/expired/refunded. A null must
/// surface as the privacy-preserving 404
/// (<see cref="ConversationErrors.NotFound"/>) and never reveal which check
/// failed.
/// </summary>
/// <summary>
/// A booking open target resolved for either creation or reopening:
/// <see cref="Counterpart"/> is the backend-resolved other party, and
/// <see cref="IsActive"/> tells whether the booking is currently eligible
/// for a NEW conversation (non-terminal). A terminal booking still resolves
/// its parties so an existing conversation's retained history can reopen;
/// creating a new conversation on it stays denied.
/// </summary>
public sealed record BookingOpenTarget(
    UserId Counterpart,
    bool IsActive);

public interface IChatBookingGateway
{
    Task<UserId?> ResolveCounterpartAsync(
        BookingId bookingId,
        UserId openerUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the counterpart even when the booking is terminal
    /// (cancelled/expired/refunded), so retained-history reopening does not
    /// depend on current booking state. Null only when the booking is
    /// unknown or the opener was never a party to it. The default derives
    /// from the active-only resolution (terminal bookings report null);
    /// implementations override with a true reopen path.
    /// </summary>
    async Task<BookingOpenTarget?> ResolveForReopenAsync(
        BookingId bookingId,
        UserId openerUserId,
        CancellationToken cancellationToken = default)
    {
        UserId? counterpart = await ResolveCounterpartAsync(
                bookingId,
                openerUserId,
                cancellationToken)
            .ConfigureAwait(false);

        return counterpart is null
            ? null
            : new BookingOpenTarget(counterpart.Value, IsActive: true);
    }
}
