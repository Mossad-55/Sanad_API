using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.RateLimiting;

/// <summary>
/// Abuse-control gate for Chat writes (single-instance in-memory sliding
/// windows). Expensive writes are rejected before full buffering where the
/// call path allows it (uploads enter a concurrency slot first).
/// </summary>
public interface IChatRateGate
{
    bool TryAcquireSend(
        UserId userId,
        ConversationId conversationId,
        out TimeSpan retryAfter);

    bool TryAcquireOpen(UserId userId, out TimeSpan retryAfter);

    bool TryEnterUpload(UserId userId, out TimeSpan retryAfter);

    void ExitUpload(UserId userId);

    bool TryAcquireUploadRate(UserId userId, out TimeSpan retryAfter);

    bool TryAcquireReport(UserId userId, out TimeSpan retryAfter);

    bool TryAcquireSync(
        UserId userId,
        ConversationId conversationId,
        out TimeSpan retryAfter);

    /// <summary>Hub negotiation budget (30/min/user default).</summary>
    bool TryAcquireNegotiate(UserId userId, out TimeSpan retryAfter);
}

/// <summary>
/// Sliding-window-log gate: each acquisition records an instant and drops
/// instants outside the window, so there is no reset burst at clock
/// boundaries. `Retry-After` is the earliest instant leaving the window.
/// Single-instance by design (matches the Chat Plus topology); entry lists
/// are capped at their limit.
/// </summary>
public sealed class InMemoryChatRateGate : IChatRateGate
{
    private readonly ChatRateOptions _options;
    private readonly Func<DateTime> _utcNow;
    private readonly object _sync = new();
    private readonly Dictionary<string, List<DateTime>> _windows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _uploadSlots = new(StringComparer.Ordinal);

    public InMemoryChatRateGate(
        ChatRateOptions? options = null,
        Func<DateTime>? utcNow = null)
    {
        _options = options ?? new ChatRateOptions();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public bool TryAcquireSend(
        UserId userId,
        ConversationId conversationId,
        out TimeSpan retryAfter)
    {
        DateTime now = _utcNow();
        lock (_sync)
        {
            bool userOk = TryAcquireLocked(
                $"send:user:{userId.Value:N}",
                _options.SendsPerMinutePerUser,
                TimeSpan.FromMinutes(1),
                now,
                out TimeSpan userRetry);

            if (!userOk)
            {
                retryAfter = userRetry;
                return false;
            }

            bool conversationOk = TryAcquireLocked(
                $"send:conversation:{userId.Value:N}:{conversationId.Value:N}",
                _options.SendsPerMinutePerConversation,
                TimeSpan.FromMinutes(1),
                now,
                out TimeSpan conversationRetry);

            if (!conversationOk)
            {
                // Roll back the user-level token so a conversation-level
                // denial does not consume the user's global budget.
                RemoveLastLocked($"send:user:{userId.Value:N}");
                retryAfter = conversationRetry;
                return false;
            }

            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public bool TryAcquireOpen(UserId userId, out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            return TryAcquireLocked(
                $"open:user:{userId.Value:N}",
                _options.OpensPerMinutePerUser,
                TimeSpan.FromMinutes(1),
                _utcNow(),
                out retryAfter);
        }
    }

    public bool TryEnterUpload(UserId userId, out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            string key = $"upload:slots:{userId.Value:N}";
            _uploadSlots.TryGetValue(key, out int active);

            if (active >= _options.UploadConcurrentPerUser)
            {
                retryAfter = TimeSpan.FromSeconds(5);
                return false;
            }

            _uploadSlots[key] = active + 1;
            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public void ExitUpload(UserId userId)
    {
        lock (_sync)
        {
            string key = $"upload:slots:{userId.Value:N}";

            if (_uploadSlots.TryGetValue(key, out int active) && active > 0)
            {
                _uploadSlots[key] = active - 1;
            }
        }
    }

    public bool TryAcquireUploadRate(UserId userId, out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            return TryAcquireLocked(
                $"upload:user:{userId.Value:N}",
                _options.UploadsPerMinutePerUser,
                TimeSpan.FromMinutes(1),
                _utcNow(),
                out retryAfter);
        }
    }

    public bool TryAcquireReport(UserId userId, out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            return TryAcquireLocked(
                $"report:user:{userId.Value:N}",
                _options.ReportsPerHourPerUser,
                TimeSpan.FromHours(1),
                _utcNow(),
                out retryAfter);
        }
    }

    public bool TryAcquireSync(
        UserId userId,
        ConversationId conversationId,
        out TimeSpan retryAfter)
    {
        DateTime now = _utcNow();
        lock (_sync)
        {
            bool userOk = TryAcquireLocked(
                $"sync:user:{userId.Value:N}",
                _options.SyncPerMinutePerUser,
                TimeSpan.FromMinutes(1),
                now,
                out TimeSpan userRetry);

            if (!userOk)
            {
                retryAfter = userRetry;
                return false;
            }

            bool conversationOk = TryAcquireLocked(
                $"sync:conversation:{userId.Value:N}:{conversationId.Value:N}",
                _options.SyncPerMinutePerConversation,
                TimeSpan.FromMinutes(1),
                now,
                out TimeSpan conversationRetry);

            if (!conversationOk)
            {
                RemoveLastLocked($"sync:user:{userId.Value:N}");
                retryAfter = conversationRetry;
                return false;
            }

            retryAfter = TimeSpan.Zero;
            return true;
        }
    }

    public bool TryAcquireNegotiate(UserId userId, out TimeSpan retryAfter)
    {
        lock (_sync)
        {
            return TryAcquireLocked(
                $"negotiate:user:{userId.Value:N}",
                _options.NegotiatePerMinutePerUser,
                TimeSpan.FromMinutes(1),
                _utcNow(),
                out retryAfter);
        }
    }

    private bool TryAcquireLocked(
        string key,
        int limit,
        TimeSpan window,
        DateTime now,
        out TimeSpan retryAfter)
    {
        if (!_windows.TryGetValue(key, out List<DateTime>? instants))
        {
            instants = new List<DateTime>(Math.Max(1, Math.Min(limit, 512)));
            _windows[key] = instants;
        }

        DateTime cutoff = now - window;
        instants.RemoveAll(instant => instant <= cutoff);

        if (instants.Count >= limit)
        {
            retryAfter = (instants[0] + window) - now;

            if (retryAfter < TimeSpan.Zero)
            {
                retryAfter = TimeSpan.Zero;
            }

            return false;
        }

        instants.Add(now);
        retryAfter = TimeSpan.Zero;
        return true;
    }

    private void RemoveLastLocked(string key)
    {
        if (_windows.TryGetValue(key, out List<DateTime>? instants) &&
            instants.Count > 0)
        {
            instants.RemoveAt(instants.Count - 1);
        }
    }
}
