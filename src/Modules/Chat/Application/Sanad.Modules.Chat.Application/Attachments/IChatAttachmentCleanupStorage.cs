namespace Sanad.Modules.Chat.Application.Attachments;

/// <summary>
/// Private-storage port for the orphan sweep. Implementations must delete
/// ONLY exact objects below the configured private Chat-attachment root —
/// never bound files, messages, reports, general uploads, or directories.
/// The sweep holds the DB row lock (FOR UPDATE SKIP LOCKED) across the blob
/// delete, so a racing bind cannot claim a blob the sweep is deleting.
/// </summary>
public interface IChatAttachmentCleanupStorage
{
    /// <summary>
    /// Deletes one expired-unbound blob by its storage key. The key must be a
    /// valid private Chat-attachment key (<c>private/chat-attachments/…</c>);
    /// anything else is refused (the call reports <see cref="ChatAttachmentDeletionOutcome.Failed"/>).
    /// </summary>
    Task<ChatAttachmentDeletionOutcome> DeleteExpiredUnboundAsync(
        string storageKey,
        CancellationToken cancellationToken);
}
