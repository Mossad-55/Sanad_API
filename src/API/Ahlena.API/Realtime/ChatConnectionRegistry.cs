using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Realtime;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.API.Realtime;

/// <summary>
/// Single-instance in-memory connection registry. Connection ids are
/// SignalR connection ids; joined conversations are re-established by the
/// client after every reconnect. All state is lock-guarded; presence and
/// typing live in their own trackers.
/// </summary>
public sealed class ChatConnectionRegistry : IChatConnectionRegistry
{
    private readonly object _sync = new();
    private readonly Dictionary<UserId, HashSet<string>> _byUser = new();
    private readonly Dictionary<string, UserId> _userByConnection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DeviceSessionId?> _sessionByConnection = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<ConversationId>> _joined = new(StringComparer.Ordinal);

    public void AddConnection(UserId userId, string connectionId)
    {
        AddConnection(userId, connectionId, null);
    }

    public void AddConnection(UserId userId, string connectionId, DeviceSessionId? sessionId)
    {
        lock (_sync)
        {
            if (!_byUser.TryGetValue(userId, out HashSet<string>? connections))
            {
                connections = new HashSet<string>(StringComparer.Ordinal);
                _byUser[userId] = connections;
            }

            connections.Add(connectionId);
            _userByConnection[connectionId] = userId;
            _sessionByConnection[connectionId] = sessionId;

            if (!_joined.ContainsKey(connectionId))
            {
                _joined[connectionId] = new HashSet<ConversationId>();
            }
        }
    }

    public void RemoveConnection(string connectionId, out UserId? userId)
    {
        lock (_sync)
        {
            if (!_userByConnection.TryGetValue(connectionId, out UserId owner))
            {
                userId = null;
                return;
            }

            userId = owner;
            _userByConnection.Remove(connectionId);
            _sessionByConnection.Remove(connectionId);
            _joined.Remove(connectionId);

            if (_byUser.TryGetValue(owner, out HashSet<string>? connections))
            {
                connections.Remove(connectionId);

                if (connections.Count == 0)
                {
                    _byUser.Remove(owner);
                }
            }
        }
    }

    public DeviceSessionId? GetSession(string connectionId)
    {
        lock (_sync)
        {
            return _sessionByConnection.TryGetValue(connectionId, out DeviceSessionId? session)
                ? session
                : null;
        }
    }

    public IReadOnlyList<string> GetConnections(UserId userId)
    {
        lock (_sync)
        {
            return _byUser.TryGetValue(userId, out HashSet<string>? connections)
                ? connections.ToArray()
                : [];
        }
    }

    public int CountConnections(UserId userId)
    {
        lock (_sync)
        {
            return _byUser.TryGetValue(userId, out HashSet<string>? connections)
                ? connections.Count
                : 0;
        }
    }

    public void Join(string connectionId, ConversationId conversationId)
    {
        lock (_sync)
        {
            if (!_joined.TryGetValue(connectionId, out HashSet<ConversationId>? joined))
            {
                joined = new HashSet<ConversationId>();
                _joined[connectionId] = joined;
            }

            joined.Add(conversationId);
        }
    }

    public void Leave(string connectionId, ConversationId conversationId)
    {
        lock (_sync)
        {
            if (_joined.TryGetValue(connectionId, out HashSet<ConversationId>? joined))
            {
                joined.Remove(conversationId);
            }
        }
    }

    public void LeaveAll(string connectionId)
    {
        lock (_sync)
        {
            _joined.Remove(connectionId);
        }
    }

    public IReadOnlyList<ConversationId> GetJoined(string connectionId)
    {
        lock (_sync)
        {
            return _joined.TryGetValue(connectionId, out HashSet<ConversationId>? joined)
                ? joined.ToArray()
                : [];
        }
    }

    public int CountJoined(string connectionId)
    {
        lock (_sync)
        {
            return _joined.TryGetValue(connectionId, out HashSet<ConversationId>? joined)
                ? joined.Count
                : 0;
        }
    }

    public void EvictSession(DeviceSessionId sessionId)
    {
        if (sessionId == DeviceSessionId.Empty)
        {
            return;
        }

        lock (_sync)
        {
            string[] connections = _sessionByConnection
                .Where(entry =>
                    entry.Value.HasValue &&
                    entry.Value.Value == sessionId)
                .Select(entry => entry.Key)
                .ToArray();

            foreach (string connectionId in connections)
            {
                _joined.Remove(connectionId);
            }
        }
    }

    public void EvictUser(UserId userId)
    {
        lock (_sync)
        {
            if (!_byUser.TryGetValue(userId, out HashSet<string>? connections))
            {
                return;
            }

            foreach (string connectionId in connections.ToArray())
            {
                _joined.Remove(connectionId);
            }
        }
    }
}
