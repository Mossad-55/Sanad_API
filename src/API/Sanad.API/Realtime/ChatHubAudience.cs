using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Realtime;

/// <summary>
/// Scoped per-dispatch audience resolution over the Chat store. Every hub
/// dispatch consults this immediately before sending — no unchecked group
/// broadcast after an earlier membership check. Hidden-for-recipient users
/// receive nothing for that conversation; either-direction blocks suppress
/// all realtime between the pair (REST history is untouched).
/// </summary>
public sealed class ChatHubAudience(
    IChatDbContext dbContext) : IChatHubAudience
{
    /// <summary>Bounded presence-subscriber scan per broadcast.</summary>
    public const int MaxSubscriberConversations = 200;

    public async Task<ConversationAudience?> GetAudienceAsync(
        ConversationId conversationId,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == conversationId,
                cancellationToken);

        if (conversation is null)
        {
            return null;
        }

        bool blocked = await HasBlockingPairAsync(
            conversation.ParticipantOne,
            conversation.ParticipantTwo,
            cancellationToken);

        return new ConversationAudience(
            conversation.ParticipantOne,
            conversation.ParticipantTwo,
            conversation.HiddenForParticipantOne,
            conversation.HiddenForParticipantTwo,
            blocked);
    }

    public async Task<IReadOnlyList<UserId>> GetEventRecipientsAsync(
        ConversationId conversationId,
        UserId? excludeUserId,
        CancellationToken cancellationToken)
    {
        ConversationAudience? audience = await GetAudienceAsync(
            conversationId, cancellationToken);

        if (audience is null || audience.BlockedPair)
        {
            return [];
        }

        var recipients = new List<UserId>(2);

        if (!IsHidden(audience, audience.ParticipantOne) &&
            audience.ParticipantOne != excludeUserId)
        {
            recipients.Add(audience.ParticipantOne);
        }

        if (!IsHidden(audience, audience.ParticipantTwo) &&
            audience.ParticipantTwo != excludeUserId)
        {
            recipients.Add(audience.ParticipantTwo);
        }

        return recipients;
    }

    public async Task<IReadOnlyList<UserId>> GetPresenceSubscribersAsync(
        UserId subjectUserId,
        CancellationToken cancellationToken)
    {
        // Provider-safe ordering (whole-struct ordering and member access
        // do not translate on Npgsql); the scan is bounded either way.
        List<Conversation> shared = await dbContext.Conversations
            .AsNoTracking()
            .Where(conversation =>
                conversation.ParticipantOne == subjectUserId ||
                conversation.ParticipantTwo == subjectUserId)
            .OrderBy(conversation => conversation.LastActivityOnUtc)
            .Take(MaxSubscriberConversations)
            .ToListAsync(cancellationToken);

        var subscribers = new HashSet<UserId>();

        foreach (Conversation conversation in shared)
        {
            UserId other = conversation.ParticipantOne == subjectUserId
                ? conversation.ParticipantTwo
                : conversation.ParticipantOne;

            if (conversation.IsHiddenFor(other) ||
                conversation.IsHiddenFor(subjectUserId))
            {
                continue;
            }

            bool blocked = await HasBlockingPairAsync(
                subjectUserId, other, cancellationToken);

            if (!blocked)
            {
                subscribers.Add(other);
            }
        }

        return subscribers.ToArray();
    }

    public async Task<(bool ShowOnlineStatus, bool ShareLastSeen)> GetPresenceGateAsync(
        UserId subjectUserId,
        CancellationToken cancellationToken)
    {
        var settings = await dbContext.PrivacySettings
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.OwnerUserId == subjectUserId,
                cancellationToken);

        return settings is null
            ? (false, false)
            : (settings.ShowOnlineStatus, settings.ShareLastSeen);
    }

    private static bool IsHidden(ConversationAudience audience, UserId user) =>
        (user == audience.ParticipantOne && audience.HiddenForParticipantOne) ||
        (user == audience.ParticipantTwo && audience.HiddenForParticipantTwo);

    private async Task<bool> HasBlockingPairAsync(
        UserId first,
        UserId second,
        CancellationToken cancellationToken) =>
        await dbContext.Blocks
            .AsNoTracking()
            .AnyAsync(
                block =>
                    (block.BlockerUserId == first && block.BlockedUserId == second) ||
                    (block.BlockerUserId == second && block.BlockedUserId == first),
                cancellationToken);
}
