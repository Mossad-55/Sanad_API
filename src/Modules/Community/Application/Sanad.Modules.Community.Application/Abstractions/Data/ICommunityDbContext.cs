using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Blocks;
using Sanad.Modules.Community.Domain.Categories;
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
    DbSet<CommunityCategory> Categories { get; }
    DbSet<CommunityPostReport> CommunityPostReports { get; }
    DbSet<CommunityAuthorRestriction> CommunityAuthorRestrictions { get; }
    DbSet<CommunityAuthorBlock> CommunityAuthorBlocks { get; }

    // Serializes a Community author restriction check-and-insert across
    // processes. Relational providers return a transaction; non-relational
    // unit-test providers can return null.
    Task<IDbContextTransaction?> BeginCommunityAuthorRestrictionTransactionAsync(
        UserId authorId,
        CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    // Read-only moderator readback of an author's Arabic/English display
    // names. The Community schema shares the Identity database, so the lookup
    // reads identity.users directly and never returns contact data (email,
    // phone) or any other Identity field.
    Task<IReadOnlyDictionary<Guid, CommunityAuthorName>> GetCommunityAuthorNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default);

    // Personalized recommendations computed entirely in the database:
    // published posts excluding the caller's interacted posts, affinity
    // authors first, then newest with deterministic tie-breaks.
    Task<IReadOnlyList<Post>> GetRecommendedPostsAsync(
        UserId userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Minimal moderator-safe author identity projection: only the Arabic and
/// English display names, never contact data.
/// </summary>
public sealed record CommunityAuthorName(string ArabicFullName, string EnglishFullName);
