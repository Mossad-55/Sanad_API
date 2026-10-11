using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Abstractions.Data;
using Sanad.Modules.Cms.Domain.Library;

namespace Sanad.Modules.Cms.Application.Library;

public enum LibraryContentSort
{
    Newest = 0,
    MostViewed = 1
}

public sealed record LibraryContentFeedItem(
    LibraryContent Content,
    bool IsLiked,
    bool IsSaved,
    bool CanAccess);

internal static class LibraryViewerState
{
    public static async Task<IReadOnlyDictionary<Guid, (bool Liked, bool Saved)>> LoadAsync(
        ICmsDbContext dbContext,
        Guid viewerUserId,
        IReadOnlyList<LibraryContent> contents,
        CancellationToken cancellationToken)
    {
        if (viewerUserId == Guid.Empty || contents.Count == 0)
            return new Dictionary<Guid, (bool, bool)>();

        List<Guid> contentIds = contents.Select(content => content.Id.Value).ToList();
        List<LibraryContentId> typedContentIds = contents.Select(content => content.Id).ToList();
        var user = new UserId(viewerUserId);

        var interactions = await dbContext.LibraryContentInteractions
            .AsNoTracking()
            .Where(interaction =>
                interaction.UserId == user &&
                typedContentIds.Contains(interaction.ContentId))
            .Select(interaction => new { interaction.ContentId, interaction.Kind })
            .ToListAsync(cancellationToken);

        var liked = interactions
            .Where(interaction => interaction.Kind == LibraryInteractionKind.Like)
            .Select(interaction => interaction.ContentId.Value)
            .ToHashSet();
        var saved = interactions
            .Where(interaction => interaction.Kind == LibraryInteractionKind.Save)
            .Select(interaction => interaction.ContentId.Value)
            .ToHashSet();

        var state = new Dictionary<Guid, (bool, bool)>();
        foreach (Guid contentId in contentIds)
        {
            state[contentId] = (liked.Contains(contentId), saved.Contains(contentId));
        }

        return state;
    }

    public static IReadOnlyList<LibraryContentFeedItem> Attach(
        IReadOnlyList<LibraryContent> contents,
        IReadOnlyDictionary<Guid, (bool Liked, bool Saved)> state,
        bool hasPremiumAccess)
    {
        var items = new List<LibraryContentFeedItem>(contents.Count);
        foreach (LibraryContent content in contents)
        {
            state.TryGetValue(content.Id.Value, out (bool Liked, bool Saved) value);
            items.Add(new LibraryContentFeedItem(
                content, value.Liked, value.Saved,
                !content.IsPremium || hasPremiumAccess));
        }

        return items;
    }
}
