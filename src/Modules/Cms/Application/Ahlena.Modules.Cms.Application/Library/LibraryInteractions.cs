using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Cms.Application.Abstractions.Data;
using Ahlena.Modules.Cms.Domain.Library;

namespace Ahlena.Modules.Cms.Application.Library;

public sealed record SetLibraryContentSavedCommand(Guid ContentId, Guid UserId, bool Value)
    : ICommand<bool>;

public sealed class SetLibraryContentSavedCommandHandler(ICmsDbContext db)
    : ICommandHandler<SetLibraryContentSavedCommand, bool>
{
    public async Task<Result<bool>> Handle(
        SetLibraryContentSavedCommand request, CancellationToken cancellationToken) =>
        await LibraryInteractionHandler.SetAsync(
            db, request.ContentId, request.UserId, request.Value,
            LibraryInteractionKind.Save, cancellationToken);
}

public sealed record SetLibraryContentLikedCommand(Guid ContentId, Guid UserId, bool Value)
    : ICommand<bool>;

public sealed class SetLibraryContentLikedCommandHandler(ICmsDbContext db)
    : ICommandHandler<SetLibraryContentLikedCommand, bool>
{
    public async Task<Result<bool>> Handle(
        SetLibraryContentLikedCommand request, CancellationToken cancellationToken) =>
        await LibraryInteractionHandler.SetAsync(
            db, request.ContentId, request.UserId, request.Value,
            LibraryInteractionKind.Like, cancellationToken);
}

public sealed record RecordLibraryContentViewCommand(Guid ContentId) : ICommand<int>;

public sealed class RecordLibraryContentViewCommandHandler(ICmsDbContext db)
    : ICommandHandler<RecordLibraryContentViewCommand, int>
{
    public async Task<Result<int>> Handle(
        RecordLibraryContentViewCommand request, CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(request.ContentId) &&
                    item.Status == LibraryContentStatus.Published,
                cancellationToken);
        if (content is null)
            return LibraryErrors.ContentNotFound;

        content.IncrementViews();
        await db.SaveChangesAsync(cancellationToken);

        return content.ViewCount;
    }
}

internal static class LibraryInteractionHandler
{
    public static async Task<Result<bool>> SetAsync(
        ICmsDbContext db,
        Guid contentId,
        Guid userId,
        bool value,
        LibraryInteractionKind kind,
        CancellationToken cancellationToken)
    {
        LibraryContent? content = await db.LibraryContents
            .SingleOrDefaultAsync(
                item => item.Id == new LibraryContentId(contentId) &&
                    item.Status == LibraryContentStatus.Published,
                cancellationToken);
        if (content is null || userId == Guid.Empty)
            return LibraryErrors.ContentNotFound;

        var user = new UserId(userId);
        LibraryContentInteraction? existing = await db.LibraryContentInteractions
            .SingleOrDefaultAsync(
                interaction =>
                    interaction.ContentId == content.Id &&
                    interaction.UserId == user &&
                    interaction.Kind == kind,
                cancellationToken);

        if (value)
        {
            if (existing is not null)
                return true;

            db.LibraryContentInteractions.Add(LibraryContentInteraction.Create(
                content.Id, user, kind, DateTime.UtcNow));
            if (kind == LibraryInteractionKind.Like)
                content.IncrementLikes();
        }
        else
        {
            if (existing is null)
                return true;

            db.LibraryContentInteractions.Remove(existing);
            if (kind == LibraryInteractionKind.Like)
                content.DecrementLikes();
        }

        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
