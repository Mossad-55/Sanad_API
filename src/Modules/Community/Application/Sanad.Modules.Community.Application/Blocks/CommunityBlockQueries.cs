namespace Sanad.Modules.Community.Application.Blocks;

using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Blocks;

/// <summary>
/// One blocked author of the caller. <c>DisplayName</c> is the author's
/// English full name with the Arabic full name as fallback, and is
/// <c>null</c> when the identity row is missing — never a placeholder.
/// <c>AvatarUrl</c> is always <c>null</c>: Community has no publicly servable
/// avatar source (the stored Identity avatar is a private file reference only
/// its owner can read), so the honest value is null.
/// </summary>
public sealed record BlockedCommunityAuthorResponse(
    Guid AuthorId,
    string? DisplayName,
    string? AvatarUrl,
    DateTime BlockedOnUtc);

/// <summary>
/// The caller's own blocked authors, most-recent block first, paged. Per
/// viewer: never includes an author blocked by anyone else.
/// </summary>
public sealed record GetBlockedCommunityAuthorsQuery(
    UserId Requester,
    int Page = 1,
    int PageSize = 20)
    : IQuery<IReadOnlyList<BlockedCommunityAuthorResponse>>;

public sealed class GetBlockedCommunityAuthorsQueryHandler(ICommunityDbContext dbContext)
    : IQueryHandler<GetBlockedCommunityAuthorsQuery, IReadOnlyList<BlockedCommunityAuthorResponse>>
{
    public async Task<Result<IReadOnlyList<BlockedCommunityAuthorResponse>>> Handle(
        GetBlockedCommunityAuthorsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        List<CommunityAuthorBlock> blocks = await dbContext.CommunityAuthorBlocks
            .AsNoTracking()
            // Per-viewer isolation: only rows the requester created. Ordering
            // is defined as most-recent block first so clients can render a
            // stable, meaningful list.
            .Where(block => block.BlockerUserId == request.Requester)
            .OrderByDescending(block => block.CreatedOnUtc)
            .ThenByDescending(block => block.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // One batched identity lookup for the whole page: a per-row lookup
        // would be an N+1 and is never performed.
        IReadOnlyDictionary<Guid, CommunityAuthorName> authors =
            await dbContext.GetCommunityAuthorNamesAsync(
                blocks.Select(block => block.BlockedAuthorUserId.Value).ToArray(),
                cancellationToken);

        List<BlockedCommunityAuthorResponse> items = blocks
            .Select(block =>
            {
                authors.TryGetValue(
                    block.BlockedAuthorUserId.Value,
                    out CommunityAuthorName? author);

                CommunityAuthorProfile profile =
                    CommunityAuthorProfile.FromAuthorName(author);

                return new BlockedCommunityAuthorResponse(
                    block.BlockedAuthorUserId.Value,
                    profile.DisplayName,
                    profile.AvatarUrl,
                    block.CreatedOnUtc);
            })
            .ToList();

        return Result<IReadOnlyList<BlockedCommunityAuthorResponse>>.Success(items);
    }
}
