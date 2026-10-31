namespace Ahlena.Modules.Chat.Application.Attachments;

/// <summary>
/// Orphan-sweep tuning (section `ChatPlus:AttachmentCleanup`). The sweep is
/// bounded and crash-safe: every pass claims at most <see cref="BatchSize"/>
/// expired unbound attachments, deletes their blobs, then deletes the rows.
/// A crash between the blob delete and the row delete is recovered on the next
/// pass (the row is still there; the blob is already gone and treated as
/// missing). Defaults match the contracts.md proposed values.
/// </summary>
public sealed record ChatAttachmentCleanupOptions
{
    public const string SectionName = "ChatPlus:AttachmentCleanup";

    /// <summary>How often the sweep runs. Default: hourly.</summary>
    public TimeSpan SweepInterval { get; init; } = TimeSpan.FromHours(1);

    /// <summary>Maximum objects deleted per pass. Default: 100.</summary>
    public int BatchSize { get; init; } = 100;
}
