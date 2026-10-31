namespace Ahlena.Modules.Chat.Domain.Changes;

/// <summary>
/// Durable change-log entry for one conversation. Writers insert rows in the
/// same transaction as the message/receipt change with a gapless
/// per-conversation <see cref="ChatChange.Revision"/> (see
/// <see cref="Conversations.Conversation.TakeNextRevision"/>); the sync
/// endpoint replays rows in revision order. The log is retained (no pruning
/// in this package), so cursors never expire.
/// </summary>
public enum ChatChangeType
{
    MessageCreated = 1,
    MessageReceiptUpdated = 2
}
