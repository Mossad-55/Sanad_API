using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Abstractions.Data;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Conversations;

namespace Sanad.Modules.Chat.Application.Attachments;

public sealed record AttachmentFileResponse(
    string ContentType,
    Stream Content);

/// <summary>
/// Endpoint 6 — participant-only file bytes. Every denial (unknown
/// conversation, foreign requester, unknown attachment, attachment owned by
/// a different conversation, missing blob) collapses into
/// <see cref="AttachmentErrors.NotFound"/>, which the controller serves as
/// a generic privacy 404 — the caller cannot tell which check failed.
/// </summary>
public sealed record GetAttachmentFileQuery(
    UserId Requester,
    ConversationId ConversationId,
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
        Conversation? conversation = await _dbContext.Conversations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.ConversationId,
                cancellationToken);

        if (conversation is null ||
            !conversation.IsParticipant(request.Requester))
        {
            return Result<AttachmentFileResponse>.Failure(
                AttachmentErrors.NotFound);
        }

        Attachment? record = await _dbContext.Attachments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.AttachmentId &&
                        item.ConversationId == request.ConversationId,
                cancellationToken);

        if (record is null)
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
