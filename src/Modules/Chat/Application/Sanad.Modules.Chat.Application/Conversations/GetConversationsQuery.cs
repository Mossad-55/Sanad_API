using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// Endpoint 1 — paged conversation list for the requester, last activity
/// first. Conversations the requester hid are excluded; the other party's
/// copy is untouched.
/// </summary>
public sealed record GetConversationsQuery(
    UserId Requester,
    int Page = 1,
    int PageSize = 20)
    : IQuery<PagedResult<ConversationListItemResponse>>;
