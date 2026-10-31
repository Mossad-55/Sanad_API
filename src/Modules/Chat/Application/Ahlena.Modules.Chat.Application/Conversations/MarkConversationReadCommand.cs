using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.Modules.Chat.Application.Conversations;

/// <summary>
/// Endpoint 7 — record that the requester has read the conversation up to
/// a given message id. Idempotent: repeating the same call keeps a single
/// row per (conversation, user) and answers the same 204.
/// </summary>
public sealed record MarkConversationReadCommand(
    UserId Requester,
    ConversationId ConversationId,
    Guid LastReadMessageId)
    : ICommand;
