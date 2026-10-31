using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.Modules.Chat.Application.Attachments;

/// <summary>
/// Endpoint 5 — upload one conversation attachment. The file stays unbound
/// until Lane B send binds it to a single message via the domain
/// <c>Attachment.Bind()</c> invariant.
/// </summary>
public sealed record UploadAttachmentCommand(
    UserId Requester,
    ConversationId ConversationId,
    string FileName,
    string ContentType,
    long Length,
    Stream Content) : ICommand<AttachmentUploadResponse>;

/// <summary>
/// <c>FileName</c> is the sanitized echo of the caller's original name (the
/// domain does not persist names; serving uses the generated storage key).
/// </summary>
public sealed record AttachmentUploadResponse(
    Guid AttachmentId,
    string FileName,
    string ContentType,
    long SizeBytes);
