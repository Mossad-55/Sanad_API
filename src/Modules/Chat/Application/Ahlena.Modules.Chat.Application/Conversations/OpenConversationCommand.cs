using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Application.Conversations;

/// <summary>
/// Endpoint 2 — open-or-get. Exactly one target is required (Opus delta):
/// <c>Recipient</c> opens a direct chat, <c>BookingId</c> opens a booking
/// chat whose counterpart the backend resolves through
/// <see cref="IChatBookingGateway"/>; both or neither is
/// <see cref="ConversationErrors.OpenTargetInvalid"/> (400). Idempotent per
/// (normalized participant pair, booking reference): reopening returns the
/// existing conversation id with <c>Created = false</c>.
/// </summary>
public sealed record OpenConversationCommand(
    UserId Opener,
    UserId? Recipient,
    BookingId? BookingId)
    : ICommand<OpenConversationResponse>;
