using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Attachments;

public sealed class UploadAttachmentCommandHandler
    : ICommandHandler<UploadAttachmentCommand, AttachmentUploadResponse>
{
    public const string Folder = "chat-attachments";
    public const long MaximumImageBytes = 5_242_880;      // images ≤ 5 MB
    public const long MaximumVideoBytes = 52_428_800;     // video ≤ 50 MB
    public const long MaximumDocumentBytes = 10_485_760;  // PDF ≤ 10 MB

    // Lane A scope: documents are PDF-only (owner-approved); voice notes and
    // signed URLs are out of scope for V1.
    private static readonly IReadOnlyDictionary<string, long> MaximumBytesByContentType =
        new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
        {
            ["image/jpeg"] = MaximumImageBytes,
            ["image/png"] = MaximumImageBytes,
            ["image/webp"] = MaximumImageBytes,
            ["video/mp4"] = MaximumVideoBytes,
            ["application/pdf"] = MaximumDocumentBytes
        };

    private readonly IChatDbContext _dbContext;
    private readonly IFileStorage _storage;

    public UploadAttachmentCommandHandler(
        IChatDbContext dbContext,
        IFileStorage storage)
    {
        _dbContext = dbContext;
        _storage = storage;
    }

    public async Task<Result<AttachmentUploadResponse>> Handle(
        UploadAttachmentCommand request,
        CancellationToken cancellationToken)
    {
        Conversation? conversation = await _dbContext.Conversations
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        // Unknown and foreign conversations behave identically, matching the
        // privacy posture of endpoints 1/2/10 — a stranger cannot probe which
        // conversation ids exist, nor whether uploads are accepted there.
        if (conversation is null ||
            !conversation.IsParticipant(request.Requester))
        {
            return Result<AttachmentUploadResponse>.Failure(
                ConversationErrors.NotFound);
        }

        if (request.Requester == UserId.Empty ||
            request.Length <= 0)
        {
            return Result<AttachmentUploadResponse>.Failure(
                AttachmentErrors.Invalid);
        }

        string contentType =
            request.ContentType?.Trim().ToLowerInvariant() ?? string.Empty;

        if (!MaximumBytesByContentType.TryGetValue(
                contentType, out long maximumBytes))
        {
            return Result<AttachmentUploadResponse>.Failure(
                AttachmentErrors.UnsupportedType);
        }

        if (request.Length > maximumBytes)
        {
            return Result<AttachmentUploadResponse>.Failure(
                AttachmentErrors.TooLarge);
        }

        byte[] bytes;
        await using (var content = new MemoryStream())
        {
            await request.Content.CopyToAsync(content, cancellationToken);
            bytes = content.ToArray();
        }

        if (bytes.LongLength != request.Length ||
            !HasMatchingSignature(contentType, bytes))
        {
            return Result<AttachmentUploadResponse>.Failure(
                AttachmentErrors.Invalid);
        }

        await using var file = new MemoryStream(bytes, writable: false);
        var saved = await _storage.SavePrivateAsync(
            file,
            contentType,
            bytes.LongLength,
            Folder,
            maximumBytes,
            cancellationToken);

        if (saved.IsFailure)
        {
            return Result<AttachmentUploadResponse>.Failure(
                MapStorageError(saved.Error));
        }

        try
        {
            Attachment record = Attachment.Create(
                request.ConversationId,
                request.Requester,
                saved.Value.Key,
                contentType,
                bytes.LongLength,
                DateTime.UtcNow);

            _dbContext.Attachments.Add(record);
            await _dbContext.SaveChangesAsync(cancellationToken);

            return Result<AttachmentUploadResponse>.Success(
                new AttachmentUploadResponse(
                    record.Id.Value,
                    SanitizeFileName(request.FileName),
                    contentType,
                    bytes.LongLength));
        }
        catch (DbUpdateException)
        {
            await _storage.DeleteAsync(saved.Value.Key, cancellationToken);
            return Result<AttachmentUploadResponse>.Failure(
                AttachmentErrors.Invalid);
        }
        catch
        {
            await _storage.DeleteAsync(saved.Value.Key, cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Chat owns its error vocabulary; the storage layer's raw codes are
    /// translated so callers only ever see Chat.Attachment.* codes.
    /// </summary>
    private static Error MapStorageError(Error error) =>
        error.Code == StorageErrors.TooLarge.Code
            ? AttachmentErrors.TooLarge
            : error.Code == StorageErrors.UnsupportedType.Code
                ? AttachmentErrors.UnsupportedType
                : AttachmentErrors.Invalid;

    /// <summary>
    /// The stored object always uses a generated safe key; the caller's
    /// original name is only echoed in the 201 body, trimmed, with path
    /// segments collapsed and a fallback so blank names never travel.
    /// </summary>
    private static string SanitizeFileName(string? fileName)
    {
        string trimmed = (fileName ?? string.Empty).Trim();

        string lastSegment = trimmed
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .LastOrDefault() ?? string.Empty;

        return string.IsNullOrWhiteSpace(lastSegment)
            ? "file"
            : lastSegment;
    }

    /// <summary>
    /// Magic-byte verification — a matching declared content type alone is
    /// never trusted (mirrors the community image-upload precedent).
    /// </summary>
    private static bool HasMatchingSignature(string contentType, byte[] bytes) =>
        contentType switch
        {
            "image/jpeg" =>
                bytes.Length >= 3 &&
                bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff,
            "image/png" =>
                bytes.Length >= 8 &&
                bytes[0] == 137 && bytes[1] == 80 && bytes[2] == 78 && bytes[3] == 71 &&
                bytes[4] == 13 && bytes[5] == 10 && bytes[6] == 26 && bytes[7] == 10,
            "image/webp" =>
                bytes.Length >= 12 &&
                bytes[0] == (byte)'R' && bytes[1] == (byte)'I' &&
                bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                bytes[8] == (byte)'W' && bytes[9] == (byte)'E' &&
                bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
            "video/mp4" =>
                bytes.Length >= 12 &&
                bytes[4] == (byte)'f' && bytes[5] == (byte)'t' &&
                bytes[6] == (byte)'y' && bytes[7] == (byte)'p',
            "application/pdf" =>
                bytes.Length >= 5 &&
                bytes[0] == (byte)'%' && bytes[1] == (byte)'P' &&
                bytes[2] == (byte)'D' && bytes[3] == (byte)'F',
            _ => false
        };
}
