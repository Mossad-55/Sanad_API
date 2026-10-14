namespace Sanad.Modules.Chat.Application.Attachments;

/// <summary>Sanitized sweep backlog: aggregate count + bytes, no user ids or keys.</summary>
public sealed record ChatAttachmentCleanupBacklog(
    int ExpiredUnboundCount,
    long ExpiredUnboundBytes);

/// <summary>Sanitized result of one sweep pass: aggregate counts + bytes only.</summary>
public sealed record ChatAttachmentCleanupSweepResult(
    int DeletedCount,
    long DeletedBytes,
    int FailureCount);

/// <summary>
/// Sanitized observability port for the orphan sweep. The backlog is computed
/// from the Chat database (expired unbound attachments only); sweep outcomes
/// are recorded without user ids, storage keys, conversation ids, or message
/// payloads.
/// </summary>
public interface IChatAttachmentCleanupMetrics
{
    /// <summary>Current expired-unbound backlog (count + bytes).</summary>
    Task<ChatAttachmentCleanupBacklog> GetBacklogAsync(
        CancellationToken cancellationToken);

    /// <summary>Records one completed sweep pass (sanitized aggregates).</summary>
    void RecordSweep(ChatAttachmentCleanupSweepResult result);
}
