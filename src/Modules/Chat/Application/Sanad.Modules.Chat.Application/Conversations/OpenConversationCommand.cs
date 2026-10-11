using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Endpoint 2 — open-or-get. Idempotent per (normalized participant pair,
/// booking reference): reopening returns the existing conversation id with
/// <c>Created = false</c>.
/// </summary>
public sealed record OpenConversationCommand(
    UserId Opener,
    UserId Recipient,
    BookingId? BookingId)
    : ICommand<OpenConversationResponse>;
