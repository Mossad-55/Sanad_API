using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Cms.Domain.Library;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.CareRecords;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Persistence;

/// <summary>
/// Relational proof for the three additive schema changes in the
/// media/care-records/Community slice. The target is a uniquely named
/// disposable database derived from
/// <c>ConnectionStrings__IdentityIntegrationDatabase</c>; the source host must
/// be loopback on port 5432 and the generated name is verified absent before
/// creation. Only the Families, Community and CMS migrations are applied, and
/// only that exact nonce database is deleted.
/// </summary>
[Collection("SliceSchemaVerification")]
public sealed class SliceSchemaPostgresTests : IAsyncLifetime
{
    private const string ConnectionStringVariable =
        "ConnectionStrings__IdentityIntegrationDatabase";

    private static readonly string ExpectedDatabase =
        "SanadSliceVerify_" + Guid.NewGuid().ToString("N");

    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    private static string? _migratedFor;
    private static bool _createdTarget;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (!_createdTarget || string.IsNullOrWhiteSpace(_migratedFor))
            return;

        await using FamiliesDbContext families = CreateFamiliesDb(_migratedFor);
        await families.Database.EnsureDeletedAsync();
        _createdTarget = false;
        _migratedFor = null;
    }

    [SlicePostgresFact]
    public async Task New_migrations_apply_and_care_record_fk_is_restrictive()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        await using FamiliesDbContext families = CreateFamiliesDb(connectionString);
        await using CommunityDbContext community = CreateCommunityDb(connectionString);
        await using CmsDbContext cms = CreateCmsDb(connectionString);

        Assert.Contains(
            "20261023131500_AddCareRecords",
            await families.Database.GetAppliedMigrationsAsync());
        Assert.Contains(
            "20261023150000_AddPostDeletionAudit",
            await community.Database.GetAppliedMigrationsAsync());
        Assert.Contains(
            "20261023114500_AddLibraryContentVideoStorageKey",
            await cms.Database.GetAppliedMigrationsAsync());

        string deleteAction = await families.Database.SqlQueryRaw<string>(
            """
            SELECT confdeltype::text AS "Value"
            FROM pg_constraint
            WHERE conname = 'FK_care_records_elderlies_elderly_id'
            """).SingleAsync();
        string referenced = await families.Database.SqlQueryRaw<string>(
            """
            SELECT confrelid::regclass::text AS "Value"
            FROM pg_constraint
            WHERE conname = 'FK_care_records_elderlies_elderly_id'
            """).SingleAsync();

        Assert.Equal("r", deleteAction);
        Assert.Contains("elderlies", referenced, StringComparison.Ordinal);

        UserId owner = UserId.New();
        Family family = Family.Create(owner, "Slice schema family");
        Elderly elderly = Elderly.Create(
            owner,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Father,
            FullName.Create("الوالد"),
            FullName.Create("Slice Elderly"),
            Gender.Male,
            new DateOnly(1950, 1, 1),
            new DateOnly(2026, 10, 24));
        CareRecord record = CareRecord.CreateForFamily(
            elderly.Id,
            owner,
            family.Id,
            "Slice Hospital",
            new DateOnly(2026, 10, 1),
            "Slice details",
            DateTime.UtcNow);

        families.AddRange(family, elderly, record);
        await families.SaveChangesAsync();

        PostgresException fkFailure = await Assert.ThrowsAsync<PostgresException>(
            () => families.Elderlies
                .Where(item => item.Id == elderly.Id)
                .ExecuteDeleteAsync());
        // PostgreSQL reports an explicit RESTRICT action as 23001
        // (restrict_violation), distinct from the 23503 NO ACTION case.
        Assert.Equal("23001", fkFailure.SqlState);
        Assert.Equal(
            "FK_care_records_elderlies_elderly_id",
            fkFailure.ConstraintName);
        Assert.True(await families.Elderlies.AnyAsync(item => item.Id == elderly.Id));

        await families.CareRecords
            .Where(item => item.Id == record.Id)
            .ExecuteDeleteAsync();
        int removed = await families.Elderlies
            .Where(item => item.Id == elderly.Id)
            .ExecuteDeleteAsync();
        Assert.Equal(1, removed);
    }

    [SlicePostgresFact]
    public async Task Community_deletion_audit_columns_round_trip()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        int columns;
        await using (CommunityDbContext schema = CreateCommunityDb(connectionString))
        {
            columns = await schema.Database.SqlQueryRaw<int>(
                """
                SELECT COUNT(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'community'
                  AND table_name = 'Posts'
                  AND column_name IN ('Version', 'DeletedBy', 'DeletedOnUtc')
                """).SingleAsync();
        }

        Assert.Equal(3, columns);

        UserId author = UserId.New();
        UserId moderator = UserId.New();
        DateTime published = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
        DateTime deleted = published.AddMinutes(5);
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, author, published);
        post.Publish(moderator, published);
        post.Delete(author, deleted);
        Guid postId = post.Id.Value;

        await using (CommunityDbContext write = CreateCommunityDb(connectionString))
        {
            write.Posts.Add(post);
            await write.SaveChangesAsync();
        }

        await using CommunityDbContext read = CreateCommunityDb(connectionString);
        Post reloaded = await read.Posts
            .AsNoTracking()
            .SingleAsync(item => item.Id == post.Id);

        Assert.Equal(PostStatus.Deleted, reloaded.Status);
        Assert.Equal(author, reloaded.DeletedBy);
        Assert.Equal(deleted, reloaded.DeletedOnUtc);
        Assert.Equal(2, reloaded.Version);
        Assert.Equal(postId, reloaded.Id.Value);
    }

    [SlicePostgresFact]
    public async Task Library_video_storage_key_round_trips_with_pinned_length()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        int? maxLength;
        await using (CmsDbContext schema = CreateCmsDb(connectionString))
        {
            maxLength = await schema.Database.SqlQueryRaw<int?>(
                """
                SELECT character_maximum_length AS "Value"
                FROM information_schema.columns
                WHERE table_schema = 'cms'
                  AND table_name = 'library_contents'
                  AND column_name = 'video_storage_key'
                """).SingleAsync();
        }

        Assert.Equal(500, maxLength);

        const string storageKey = "library-video/2026/10/slice-proof.mp4";
        LibraryContent content = LibraryContent.Create(
            LibraryContentType.Video,
            null,
            "عنوان",
            "Title",
            "ملخص",
            "Summary",
            videoUrl: null,
            videoStorageKey: storageKey,
            durationSeconds: 120,
            coverImagePath: "library/cover.png",
            isPremium: false,
            sections: [],
            createdOnUtc: DateTime.UtcNow);

        await using (CmsDbContext write = CreateCmsDb(connectionString))
        {
            write.LibraryContents.Add(content);
            await write.SaveChangesAsync();
        }

        await using CmsDbContext read = CreateCmsDb(connectionString);
        LibraryContent reloaded = await read.LibraryContents
            .AsNoTracking()
            .SingleAsync(item => item.Id == content.Id);

        Assert.True(reloaded.HasStoredVideo);
        Assert.Equal(storageKey, reloaded.VideoStorageKey);
        Assert.Null(reloaded.VideoUrl);
    }

    private static async Task EnsureMigratedAsync(string connectionString)
    {
        if (_migratedFor == connectionString)
            return;

        await MigrationLock.WaitAsync();
        try
        {
            if (_migratedFor == connectionString)
                return;

            await EnsureFreshDatabaseAbsentAsync(connectionString);

            _createdTarget = true;

            await using (FamiliesDbContext families = CreateFamiliesDb(connectionString))
            {
                await families.Database.MigrateAsync();
            }

            await using (CommunityDbContext community = CreateCommunityDb(connectionString))
            {
                await community.Database.MigrateAsync();
            }

            await using (CmsDbContext cms = CreateCmsDb(connectionString))
            {
                await cms.Database.MigrateAsync();
            }

            _migratedFor = connectionString;
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    private static string GetValidatedConnectionString()
    {
        string? raw = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException(
                $"The slice schema proof requires {ConnectionStringVariable}.");

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1") || builder.Port != 5432)
            throw new InvalidOperationException(
                "Refusing to write: the slice verification source must be localhost:5432 PostgreSQL.");

        builder.Database = ExpectedDatabase;
        return builder.ConnectionString;
    }

    private static async Task EnsureFreshDatabaseAbsentAsync(string connectionString)
    {
        var maintenance = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Database = "postgres"
        };

        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT EXISTS (SELECT 1 FROM pg_database WHERE datname = @database)";
        command.Parameters.AddWithValue("database", ExpectedDatabase);
        bool exists = (bool)(await command.ExecuteScalarAsync() ?? false);

        if (exists)
            throw new InvalidOperationException(
                "Refusing to write: the randomly named slice target already exists.");
    }

    private static FamiliesDbContext CreateFamiliesDb(string connectionString) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", FamiliesDbContext.Schema))
            .Options);

    private static CommunityDbContext CreateCommunityDb(string connectionString) =>
        new(new DbContextOptionsBuilder<CommunityDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", CommunityDbContext.Schema))
            .Options);

    private static CmsDbContext CreateCmsDb(string connectionString) =>
        new(new DbContextOptionsBuilder<CmsDbContext>()
            .UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsHistoryTable(
                    "__EFMigrationsHistory", CmsDbContext.Schema))
            .Options);

    private sealed class SlicePostgresFactAttribute : FactAttribute
    {
        public SlicePostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = "Slice PostgreSQL proof is skipped. Set " +
                       ConnectionStringVariable +
                       " to a local PostgreSQL instance to run it against a " +
                       "uniquely named disposable target.";
            }
        }
    }
}

[CollectionDefinition("SliceSchemaVerification", DisableParallelization = true)]
public sealed class SliceSchemaVerificationCollection
{
}
