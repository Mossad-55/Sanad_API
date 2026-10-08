using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Domain.Interactions;
using Sanad.Modules.Community.Domain.Uploads;

namespace Sanad.Modules.Community.Infrastructure.Persistence;

public class CommunityDbContext : DbContext, ICommunityDbContext
{
    public const string Schema = "community";

    public CommunityDbContext(DbContextOptions<CommunityDbContext> options)
        : base(options)
    {
    }

    public DbSet<Post> Posts { get; set; } = null!;
    public DbSet<Comment> Comments { get; set; } = null!;
    public DbSet<Reply> Replies { get; set; } = null!;
    public DbSet<CheckIn> CheckIns { get; set; } = null!;
    public DbSet<Rating> Ratings { get; set; } = null!;
    public DbSet<CommunityInteraction> Interactions { get; set; } = null!;
    public DbSet<CommunityImage> CommunityImages { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommunityDbContext).Assembly);
    }
}
