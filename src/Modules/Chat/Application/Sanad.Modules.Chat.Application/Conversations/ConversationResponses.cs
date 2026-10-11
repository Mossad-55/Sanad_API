namespace Sanad.Modules.Chat.Application.Conversations;

public sealed record ParticipantResponse(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl);

public sealed record LastMessageResponse(
    Guid Id,
    string Type,
    string Text,
    DateTime SentOnUtc);

public sealed record ConversationListItemResponse(
    Guid Id,
    ParticipantResponse Participant,
    LastMessageResponse? LastMessage,
    int UnreadCount);

public sealed record OpenConversationResponse(
    Guid ConversationId,
    bool Created);

/// <summary>
/// Paged envelope identical to the Caregivers <c>PagedResult&lt;T&gt;</c>.
/// BuildingBlocks has no PagedResult and the Chat module must not reference
/// Caregivers.Application, so the shape is duplicated here — JSON output
/// matches every other paged endpoint. Seam review (Integrator): hoist to
/// BuildingBlocks and delete the duplicate if a shared type is preferred.
/// </summary>
public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);
