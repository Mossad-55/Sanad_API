using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Domain.Interactions;

namespace Sanad.Modules.Community.Application.Abstractions.Data;

public interface ICommunityDbContext
{
    DbSet<Post> Posts { get; }
    DbSet<Comment> Comments { get; }
    DbSet<Reply> Replies { get; }
    DbSet<CheckIn> CheckIns { get; }
    DbSet<Rating> Ratings { get; }
    DbSet<CommunityInteraction> Interactions { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
