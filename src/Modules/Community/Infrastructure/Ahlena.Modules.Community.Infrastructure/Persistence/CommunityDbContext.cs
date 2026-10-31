using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Domain.Blocks;
using Ahlena.Modules.Community.Domain.Categories;
using Ahlena.Modules.Community.Domain.Comments;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Domain.CheckIns;
using Ahlena.Modules.Community.Domain.Ratings;
using Ahlena.Modules.Community.Domain.Interactions;
using Ahlena.Modules.Community.Domain.Uploads;

namespace Ahlena.Modules.Community.Infrastructure.Persistence;

public class CommunityDbContext : DbContext, ICommunityDbContext
{
    public const string Schema = "community";
    private const int AuthorRestrictionLockNamespace = 0x53414E41;

    public CommunityDbContext(DbContextOptions<CommunityDbContext> options)
        : base(options)
    {
    }

    public DbSet<Post> Posts { get; set; } = null!;
    public DbSet<PostView> PostViews { get; set; } = null!;
    public DbSet<Comment> Comments { get; set; } = null!;
    public DbSet<Reply> Replies { get; set; } = null!;
    public DbSet<CheckIn> CheckIns { get; set; } = null!;
    public DbSet<Rating> Ratings { get; set; } = null!;
    public DbSet<CommunityInteraction> Interactions { get; set; } = null!;
    public DbSet<CommunityImage> CommunityImages { get; set; } = null!;
    public DbSet<CommunityCategory> Categories { get; set; } = null!;
    public DbSet<CommunityPostReport> CommunityPostReports { get; set; } = null!;
    public DbSet<CommunityAuthorRestriction> CommunityAuthorRestrictions { get; set; } = null!;
    public DbSet<CommunityAuthorBlock> CommunityAuthorBlocks { get; set; } = null!;

    public async Task<IDbContextTransaction?> BeginCommunityAuthorRestrictionTransactionAsync(
        UserId authorId,
        CancellationToken cancellationToken = default)
    {
        if (!Database.IsRelational())
            return null;

        bool isPostgres = Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL";
        IDbContextTransaction transaction = await Database.BeginTransactionAsync(
            isPostgres ? IsolationLevel.ReadCommitted : IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            if (isPostgres)
            {
                // A transaction-scoped advisory lock is shared by every API
                // process. ReadCommitted gives the post-lock existence query
                // a fresh snapshot after any prior lock holder commits.
                int authorLockKey = authorId.Value.GetHashCode();
                await Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT pg_advisory_xact_lock({AuthorRestrictionLockNamespace}, {authorLockKey})",
                    cancellationToken);
            }

            return transaction;
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }
    }

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
              AND NOT EXISTS (
                SELECT 1 FROM {{schema}}"CommunityAuthorBlocks" b
                WHERE b."BlockerUserId" = @userId AND b."BlockedAuthorUserId" = p."AuthorId")
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

    public async Task<IReadOnlyDictionary<Guid, CommunityAuthorName>> GetCommunityAuthorNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken = default)
    {
        var names = new Dictionary<Guid, CommunityAuthorName>();
        if (userIds is null || userIds.Count == 0)
        {
            return names;
        }

        // The identity schema lives in the shared database. A non-relational
        // provider (the in-memory unit-test seam) has no such table, so the
        // lookup degrades to "no names" instead of throwing; the caller then
        // projects an honest null display name.
        if (!Database.IsRelational())
        {
            return names;
        }

        // SQLite (unit-test seam) has no Identity schema; production
        // PostgreSQL stores Identity users in the identity schema. The prefix
        // is a fixed internal constant in both cases, never user input.
        string prefix = Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite"
            ? "main."
            : "identity.";

        Guid[] distinct = userIds.Distinct().ToArray();

        var command = Database.GetDbConnection().CreateCommand();
        var placeholders = new string[distinct.Length];
        for (int i = 0; i < distinct.Length; i++)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = $"@id{i}";
            parameter.DbType = System.Data.DbType.Guid;
            parameter.Value = distinct[i];
            command.Parameters.Add(parameter);
            placeholders[i] = parameter.ParameterName;
        }

        command.CommandText =
            "SELECT id AS \"UserId\", arabic_full_name AS \"ArabicFullName\", " +
            "english_full_name AS \"EnglishFullName\", avatar_url AS \"AvatarKey\" " +
            $"FROM {prefix}users WHERE id IN ({string.Join(", ", placeholders)})";

        bool openedHere = Database.GetDbConnection().State != System.Data.ConnectionState.Open;
        if (openedHere)
        {
            await Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                Guid id = reader.GetGuid(0);
                string arabic = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                string english = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                string? avatarKey = reader.IsDBNull(3) ? null : reader.GetString(3);
                names[id] = new CommunityAuthorName(id, arabic, english, avatarKey);
            }
        }
        finally
        {
            if (openedHere)
            {
                await Database.CloseConnectionAsync();
            }
        }

        return names;
    }
}
