using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Domain.Blocks;
using Ahlena.Modules.Community.Domain.Categories;
using Ahlena.Modules.Community.Domain.Comments;
using Ahlena.Modules.Community.Domain.CheckIns;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Domain.Ratings;
using Ahlena.Modules.Community.Domain.Interactions;
using Ahlena.Modules.Community.Domain.Uploads;

namespace Ahlena.Modules.Community.Application.Abstractions.Data;

public interface ICommunityDbContext
{
    DbSet<Post> Posts { get; }
    DbSet<PostView> PostViews { get; }
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
    // names plus the stored Identity private avatar key. The Community schema
    // shares the Identity database, so the lookup reads identity.users
    // directly and never returns contact data (email, phone) or any other
    // Identity field. The avatar key is a private storage reference consumed
    // only by the authorized avatar download route; it is never serialized.
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
/// Minimal moderator-safe author identity projection: the Arabic and English
/// display names plus, since the S3 approved photo route (refund decision lock
/// #32), the stored Identity private avatar key. The avatar key is a private
/// storage reference (<c>private/avatars/…</c>) — it is never contact data and
/// never serialized; it is consumed only by the authorized avatar download
/// route, which streams the bytes after enforcing anonymity.
/// </summary>
public sealed record CommunityAuthorName(
    Guid UserId,
    string ArabicFullName,
    string EnglishFullName,
    string? AvatarKey = null);
