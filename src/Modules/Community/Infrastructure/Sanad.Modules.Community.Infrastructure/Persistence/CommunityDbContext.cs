using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Application.Abstractions.Data;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Domain.CheckIns;
using Sanad.Modules.Community.Domain.Ratings;
using Sanad.Modules.Community.Domain.Interactions;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CommunityDbContext).Assembly);
    }

    public async Task<IReadOnlyList<Post>> GetRecommendedPostsAsync(
        UserId userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // SQLite (unit-test seam) has no schemas; production PostgreSQL
        // requires the schema-qualified tables. The prefix is a fixed
        // internal constant in both cases, never user input.
        string schema = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? string.Empty
            : "community.";

        string sql = $$"""
            SELECT p.* FROM {{schema}}"Posts" p
            WHERE p."Status" = 2
              AND NOT EXISTS (
                SELECT 1 FROM {{schema}}"Interactions" i
                WHERE i."TargetType" = 1 AND i."TargetId" = p."Id" AND i."UserId" = @userId)
              AND NOT EXISTS (
                SELECT 1 FROM {{schema}}"Comments" c
                WHERE c."PostId" = p."Id" AND c."AuthorId" = @userId)
              AND NOT EXISTS (
                SELECT 1 FROM {{schema}}"Ratings" r
                WHERE r."PostId" = p."Id" AND r."UserId" = @userId)
              AND NOT EXISTS (
                SELECT 1 FROM {{schema}}"CheckIns" h
                WHERE h."PostId" = p."Id" AND h."UserId" = @userId)
            ORDER BY (
                EXISTS (
                    SELECT 1 FROM {{schema}}"Posts" a
                    WHERE a."AuthorId" = p."AuthorId" AND a."AuthorId" <> @userId
                      AND (EXISTS (
                             SELECT 1 FROM {{schema}}"Interactions" i2
                             WHERE i2."TargetType" = 1 AND i2."TargetId" = a."Id" AND i2."UserId" = @userId)
                        OR EXISTS (
                             SELECT 1 FROM {{schema}}"Comments" c2
                             WHERE c2."PostId" = a."Id" AND c2."AuthorId" = @userId)
                        OR EXISTS (
                             SELECT 1 FROM {{schema}}"Ratings" r2
                             WHERE r2."PostId" = a."Id" AND r2."UserId" = @userId)
                        OR EXISTS (
                             SELECT 1 FROM {{schema}}"CheckIns" h2
                             WHERE h2."PostId" = a."Id" AND h2."UserId" = @userId)))) DESC,
              p."CreatedOnUtc" DESC,
              (p."LikesCount" + p."FavoritesCount" + p."CommentsCount") DESC,
              p."Id"
            LIMIT @take OFFSET @skip
            """;

        return await Posts.FromSqlRaw(
                sql,
                CreateParameter("userId", userId.Value, System.Data.DbType.Guid),
                CreateParameter("take", pageSize, System.Data.DbType.Int32),
                CreateParameter("skip", (page - 1) * pageSize, System.Data.DbType.Int32))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    private System.Data.Common.DbParameter CreateParameter(
        string name,
        object value,
        System.Data.DbType dbType)
    {
        System.Data.Common.DbCommand command = Database.GetDbConnection().CreateCommand();
        System.Data.Common.DbParameter parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.DbType = dbType;
        parameter.Value = value;
        return parameter;
    }
}
