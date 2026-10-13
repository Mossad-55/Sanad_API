using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Realtime;

/// <summary>
/// Single-instance in-memory presence tracker. Online = any live device
/// with heartbeat age under 90s (OR across devices). Last-seen is the last
/// accepted heartbeat instant only. At most one accepted heartbeat per 30s
/// per connection; excessive refreshes are dropped. Restart loses all
/// state (unknown/null until new heartbeats); nothing is fabricated from
/// login or device-registration timestamps.
/// </summary>
public sealed class PresenceTracker : IPresenceTracker
{
    public static readonly TimeSpan OnlineWindow = TimeSpan.FromSeconds(90);
    public static readonly TimeSpan HeartbeatMinimumInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();

    // connectionId -> (user, last accepted heartbeat)
    private readonly Dictionary<string, (UserId UserId, DateTime At)> _heartbeats =
        new(StringComparer.Ordinal);

    // user -> last accepted heartbeat on any device (survives disconnects,
    // lost only on restart). Offline users still report last seen when the
    // subject shares it.
    private readonly Dictionary<UserId, DateTime> _lastSeen = new();

    public PresenceTracker()
    {
    }

    public bool Heartbeat(UserId userId, string connectionId, DateTime now)
    {
        lock (_sync)
        {
            if (_heartbeats.TryGetValue(connectionId, out var existing) &&
                existing.UserId == userId &&
                now - existing.At < HeartbeatMinimumInterval)
            {
                return false;
            }

            _heartbeats[connectionId] = (userId, now);

            if (!_lastSeen.TryGetValue(userId, out DateTime seen) || now > seen)
            {
                _lastSeen[userId] = now;
            }

            return true;
        }
    }

    public void RemoveConnection(string connectionId, DateTime now)
    {
        lock (_sync)
        {
            _heartbeats.Remove(connectionId);
        }
    }

    public bool IsOnline(UserId userId, DateTime now)
    {
        lock (_sync)
        {
            foreach (var entry in _heartbeats.Values)
            {
                if (entry.UserId == userId && now - entry.At < OnlineWindow)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public DateTime? GetLastSeen(UserId userId, DateTime now)
    {
        lock (_sync)
        {
            return _lastSeen.TryGetValue(userId, out DateTime seen) && seen <= now
                ? seen
                : null;
        }
    }
}
