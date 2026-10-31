using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Domain.Attachments;
using Ahlena.Modules.Chat.Domain.Conversations;

namespace Ahlena.Modules.Chat.Application.Attachments;

public sealed record AttachmentFileResponse(
    string ContentType,
    Stream Content);

/// <summary>
/// Endpoint 6 — participant-only file bytes. The route carries the
/// attachment id only (Opus delta): the participant check runs against the
/// conversation the attachment itself belongs to, so the client can never
/// steer it with a conversation id. Every denial (unknown attachment,
/// foreign requester, missing blob) collapses into
/// <see cref="AttachmentErrors.NotFound"/>, which the controller serves as
/// a generic privacy 404 — the caller cannot tell which check failed.
/// </summary>
public sealed record GetAttachmentFileQuery(
    UserId Requester,
    AttachmentId AttachmentId) : IQuery<AttachmentFileResponse>;

public sealed class GetAttachmentFileQueryHandler
    : IQueryHandler<GetAttachmentFileQuery, AttachmentFileResponse>
{
    private readonly IChatDbContext _dbContext;
    private readonly IFileStorage _storage;

    public GetAttachmentFileQueryHandler(
        IChatDbContext dbContext,
        IFileStorage storage)
    {
        _dbContext = dbContext;
        _storage = storage;
    }

    public async Task<Result<AttachmentFileResponse>> Handle(
        GetAttachmentFileQuery request,
        CancellationToken cancellationToken)
    {
        Attachment? record = await _dbContext.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.AttachmentId,
                cancellationToken);

        if (record is null)
        {
            return Result<AttachmentFileResponse>.Failure(
                AttachmentErrors.NotFound);
        }

        Conversation? conversation = await _dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == record.ConversationId,
                cancellationToken);

        if (conversation is null ||
            !conversation.IsParticipant(request.Requester))
        {
            return Result<AttachmentFileResponse>.Failure(
                AttachmentErrors.NotFound);
        }

        var opened = await _storage.OpenReadAsync(
            record.StorageKey,
            cancellationToken);

        if (opened.IsFailure)
        {
            return Result<AttachmentFileResponse>.Failure(
                AttachmentErrors.NotFound);
        }

        return Result<AttachmentFileResponse>.Success(
            new AttachmentFileResponse(
                opened.Value.ContentType,
                opened.Value.Content));
    }
}
