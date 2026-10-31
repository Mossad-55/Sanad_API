namespace Ahlena.Modules.Chat.Application.Attachments;

/// <summary>
/// Result of one physical blob deletion during the orphan sweep. The sweep
/// never deletes a DB row whose blob deletion did not resolve to
/// <see cref="Deleted"/> or <see cref="Missing"/> — a <see cref="Failed"/>
/// blob keeps its row so the next pass retries it.
/// </summary>
public enum ChatAttachmentDeletionOutcome
{
    /// <summary>The blob existed below the private root and was deleted.</summary>
    Deleted,

    /// <summary>The blob was already gone (crash between blob and row delete, or
    /// a concurrent cleanup); the row may be deleted.</summary>
    Missing,

    /// <summary>The blob deletion failed (I/O error, lock, permission); the
    /// row is kept for the next pass.</summary>
    Failed
}
