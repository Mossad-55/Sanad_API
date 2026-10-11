using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Open-time eligibility gate for endpoint 2. Implemented in Sanad.API
/// (recipient existence plus scope/booking-reference consistency); the
/// Chat Application only declares the seam so it never takes a cross-module
/// project reference. A false result must surface as the privacy-preserving
/// 404 (<see cref="ConversationErrors.NotFound"/>) and never reveal why.
/// </summary>
public interface IChatEligibilityChecker
{
    Task<bool> CanOpenAsync(
        UserId openerUserId,
        UserId recipientUserId,
        BookingId? bookingId,
        CancellationToken cancellationToken = default);
}
