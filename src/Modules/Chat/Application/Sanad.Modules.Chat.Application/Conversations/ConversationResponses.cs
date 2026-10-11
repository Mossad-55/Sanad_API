namespace Sanad.Modules.Chat.Application.Conversations;

/// <summary>
/// The conversation's counterpart. <c>accountType</c> is the Identity
/// <c>AccountType</c> integer (Opus delta; null only when the Identity user
/// row is gone). For care-home owners <c>displayName</c>/<c>avatarUrl</c>
/// resolve to the facility's name and cover over the owner's user id.
/// </summary>
public sealed record ParticipantResponse(
    Guid UserId,
    string DisplayName,
    string? AvatarUrl,
    int? AccountType);

public sealed record LastMessageResponse(
    Guid Id,
    string Type,
    string Text,
    DateTime CreatedOnUtc);

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
