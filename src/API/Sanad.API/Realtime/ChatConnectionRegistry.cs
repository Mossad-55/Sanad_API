using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Realtime;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.API.Realtime;

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
    private readonly Dictionary<string, HashSet<ConversationId>> _joined = new(StringComparer.Ordinal);

    public void AddConnection(UserId userId, string connectionId)
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
}
