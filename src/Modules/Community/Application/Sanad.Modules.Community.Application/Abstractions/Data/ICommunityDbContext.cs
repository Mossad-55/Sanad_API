using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Uploads;

namespace Sanad.Modules.Community.Application.Abstractions.Data;

public interface ICommunityDbContext
{
    DbSet<Post> Posts { get; }
    DbSet<Comment> Comments { get; }
    DbSet<Reply> Replies { get; }
    DbSet<CheckIn> CheckIns { get; }
    DbSet<Rating> Ratings { get; }
    DbSet<CommunityInteraction> Interactions { get; }
    DbSet<CommunityImage> CommunityImages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Personalized recommendations computed entirely in the database:
    // published posts excluding the caller's interacted posts, affinity
    // authors first, then newest with deterministic tie-breaks.
    Task<IReadOnlyList<Post>> GetRecommendedPostsAsync(
        UserId userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
