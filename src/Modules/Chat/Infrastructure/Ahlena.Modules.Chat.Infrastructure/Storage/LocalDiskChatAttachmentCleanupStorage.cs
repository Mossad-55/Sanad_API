using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.Chat.Application.Attachments;

namespace Ahlena.Modules.Chat.Infrastructure.Storage;

/// <summary>
/// Local-disk implementation of the orphan-sweep storage port. Deletes ONLY
/// exact objects below the configured private Chat-attachment root
/// (<c>{Storage:Local:RootPath}-private/private/chat-attachments/…</c>).
/// Any key that is not a normalized <c>private/chat-attachments/…</c> key is
/// refused, so the sweep can never touch bound files, messages, reports,
/// general uploads, or directories. A missing blob is <see cref="ChatAttachmentDeletionOutcome.Missing"/>
/// (recoverable); an I/O failure is <see cref="ChatAttachmentDeletionOutcome.Failed"/>
/// (the row is kept for the next pass).
/// </summary>
public sealed class LocalDiskChatAttachmentCleanupStorage
    : IChatAttachmentCleanupStorage
{
    private const string PrivateKeyPrefix = "private/";
    private const string ChatAttachmentFolder = "chat-attachments/";

    private readonly LocalStorageOptions _options;

    public LocalDiskChatAttachmentCleanupStorage(
        IOptions<LocalStorageOptions> options)
    {
        _options = options.Value;
    }

    public Task<ChatAttachmentDeletionOutcome> DeleteExpiredUnboundAsync(
        string storageKey,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        string normalizedKey = (storageKey ?? string.Empty)
            .Trim()
            .Replace('\\', '/')
            .Trim('/');

        // Exact-provenance guard: only normalized private Chat-attachment
        // keys are deletable. This is defense-in-depth on top of the sweep's
        // DB-level (IsBound = false) filter.
        if (!normalizedKey.StartsWith(
                PrivateKeyPrefix + ChatAttachmentFolder,
                StringComparison.Ordinal) ||
            normalizedKey.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalizedKey))
        {
            return Task.FromResult(ChatAttachmentDeletionOutcome.Failed);
        }

        try
        {
            string fullPath = ResolvePrivatePath(normalizedKey);

            if (!File.Exists(fullPath))
            {
                return Task.FromResult(ChatAttachmentDeletionOutcome.Missing);
            }

            File.Delete(fullPath);

            return Task.FromResult(ChatAttachmentDeletionOutcome.Deleted);
        }
        catch (Exception)
        {
            return Task.FromResult(ChatAttachmentDeletionOutcome.Failed);
        }
    }

    private string ResolvePrivatePath(string normalizedKey)
    {
        string root = Path.GetFullPath(
            _options.GetEffectiveRootPath()
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) + "-private");
        string fullPath = Path.GetFullPath(Path.Combine(
            root,
            normalizedKey.Replace('/', Path.DirectorySeparatorChar)));
        string relativePath = Path.GetRelativePath(root, fullPath);

        if (Path.IsPathRooted(relativePath) ||
            relativePath == ".." ||
            relativePath.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Chat attachment cleanup key resolves outside private storage.");
        }

        return fullPath;
    }
}
