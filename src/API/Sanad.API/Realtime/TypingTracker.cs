using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Realtime;

/// <summary>
/// Single-instance in-memory typing tracker. At most one accepted hint per
/// 2s per user/conversation; a start expires 8s after the last refresh;
/// stop and disconnect clear. Expiry instants are server-set; the dispatch
/// loop broadcasts stops for swept hints while the pair is still eligible.
/// </summary>
public sealed class TypingTracker : ITypingTracker
{
    public static readonly TimeSpan RefreshMinimumInterval = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan HintLifetime = TimeSpan.FromSeconds(8);

    private readonly object _sync = new();
    private readonly Dictionary<(UserId, ConversationId), Hint> _hints = new();

    private sealed record Hint(
        UserId UserId,
        ConversationId ConversationId,
        string ConnectionId,
        DateTime LastRefresh,
        DateTime ExpiresOnUtc);

    public bool Refresh(
        UserId userId,
        ConversationId conversationId,
        string connectionId,
        bool isTyping,
        DateTime now,
        out DateTime expiresOnUtc)
    {
        lock (_sync)
        {
            if (!isTyping)
            {
                _hints.Remove((userId, conversationId));
                expiresOnUtc = now;
                return true;
            }

            if (_hints.TryGetValue((userId, conversationId), out Hint? existing) &&
                now - existing.LastRefresh < RefreshMinimumInterval)
            {
                expiresOnUtc = existing.ExpiresOnUtc;
                return false;
            }

            expiresOnUtc = now + HintLifetime;
            _hints[(userId, conversationId)] = new Hint(
                userId, conversationId, connectionId, now, expiresOnUtc);
            return true;
        }
    }

    public IReadOnlyList<(UserId UserId, ConversationId ConversationId)> ClearConnection(
        string connectionId)
    {
        lock (_sync)
        {
            List<(UserId, ConversationId)> cleared = _hints.Values
                .Where(hint => string.Equals(
                    hint.ConnectionId, connectionId, StringComparison.Ordinal))
                .Select(hint => (hint.UserId, hint.ConversationId))
                .ToList();

            foreach (var key in cleared)
            {
                _hints.Remove(key);
            }

            return cleared;
        }
    }

    public void ClearUser(UserId userId, ConversationId conversationId)
    {
        lock (_sync)
        {
            _hints.Remove((userId, conversationId));
        }
    }

    public IReadOnlyList<(UserId UserId, ConversationId ConversationId)> SweepExpired(
        DateTime now)
    {
        lock (_sync)
        {
            List<(UserId, ConversationId)> expired = _hints.Values
                .Where(hint => hint.ExpiresOnUtc <= now)
                .Select(hint => (hint.UserId, hint.ConversationId))
                .ToList();

            foreach (var key in expired)
            {
                _hints.Remove(key);
            }

            return expired;
        }
    }
}
