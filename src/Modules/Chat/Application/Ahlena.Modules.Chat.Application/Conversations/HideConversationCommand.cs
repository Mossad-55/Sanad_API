using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.Modules.Chat.Application.Conversations;

/// <summary>
/// Endpoint 10 — hide the conversation for the requester only (documented
/// as delete). Idempotent; nothing is erased and the other party keeps the
/// history.
/// </summary>
public sealed record HideConversationCommand(
    UserId Requester,
    ConversationId ConversationId)
    : ICommand;
