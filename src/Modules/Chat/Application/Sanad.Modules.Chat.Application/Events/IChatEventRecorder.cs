using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Domain.Messages;

namespace Sanad.Modules.Chat.Application.Events;

/// <summary>
/// In-transaction recorder for durable Chat changes. Implementations write
/// change-log and notification-intent rows through the same
/// <see cref="Abstractions.Data.IChatDbContext"/> the handler saves, so the
/// rows commit atomically with the message/receipt change and a crash before
/// commit loses nothing. Dispatch (hub fan-out, Notifications producer)
/// happens after commit from the dispatcher, never inline.
/// </summary>
public interface IChatEventRecorder
{
    /// <summary>Records a created message + its notification intent.</summary>
    void RecordMessageCreated(
        Conversation conversation,
        Message message,
        UserId recipientUserId,
        DateTime now);

    /// <summary>Records a first delivery/read transition of a message.</summary>
    void RecordReceiptUpdated(
        Conversation conversation,
        Message message,
        DateTime now);
}
