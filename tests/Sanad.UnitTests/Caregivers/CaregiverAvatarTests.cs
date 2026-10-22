using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers;

/// <summary>
/// S3 approved photo route (refund decision lock #32) for a caregiver's avatar.
/// The route resolves the caregiver through the SAME discoverability predicate
/// the public search uses — Active status, <c>show_profile</c> not disabled and
/// the identity join — so an unknown, inactive or hidden caregiver is an honest
/// generic 404. The stored Identity private <c>avatar_url</c> key is never
/// returned on the wire; the route streams bytes.
/// </summary>
public sealed class CaregiverAvatarTests
{
    [Fact]
    public async Task Photo_resolves_the_avatar_key_for_an_active_discoverable_caregiver()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();
        CaregiverAvatarFixture.Seed seed = fixture.SeedCaregiver(
            status: 4,
            showProfile: true,
            avatarKey: "private/avatars/active.jpg");

        string? key = await fixture.Db.GetDiscoverableCaregiverAvatarKeyAsync(seed.CaregiverId);

        Assert.Equal("private/avatars/active.jpg", key);
    }

    [Fact]
    public async Task Photo_key_is_null_for_a_non_active_caregiver()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();
        CaregiverAvatarFixture.Seed seed = fixture.SeedCaregiver(
            status: 2,
            showProfile: true,
            avatarKey: "private/avatars/pending.jpg");

        string? key = await fixture.Db.GetDiscoverableCaregiverAvatarKeyAsync(seed.CaregiverId);

        Assert.Null(key);
    }

    [Fact]
    public async Task Photo_key_is_null_when_the_caregiver_hid_the_profile()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();
        CaregiverAvatarFixture.Seed seed = fixture.SeedCaregiver(
            status: 4,
            showProfile: false,
            avatarKey: "private/avatars/hidden.jpg");

        string? key = await fixture.Db.GetDiscoverableCaregiverAvatarKeyAsync(seed.CaregiverId);

        Assert.Null(key);
    }

    [Fact]
    public async Task Photo_key_is_null_for_an_unknown_caregiver()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();

        string? key = await fixture.Db.GetDiscoverableCaregiverAvatarKeyAsync(CaregiverId.New());

        Assert.Null(key);
    }

    [Fact]
    public async Task Photo_key_is_null_when_the_identity_row_is_missing()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();
        CaregiverAvatarFixture.Seed seed = fixture.SeedCaregiver(
            status: 4,
            showProfile: true,
            avatarKey: null,
            withIdentity: false);

        string? key = await fixture.Db.GetDiscoverableCaregiverAvatarKeyAsync(seed.CaregiverId);

        Assert.Null(key);
    }

    [Fact]
    public async Task Photo_handler_streams_bytes_and_never_returns_the_raw_key()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();
        CaregiverAvatarFixture.Seed seed = fixture.SeedCaregiver(
            status: 4,
            showProfile: true,
            avatarKey: "private/avatars/active.jpg");
        var storage = new RecordingStorage();

        var result = await new GetCaregiverPhotoQueryHandler(fixture.Db, storage).Handle(
            new GetCaregiverPhotoQuery(seed.CaregiverId),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.Equal("private/avatars/active.jpg", storage.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_handler_returns_generic_not_found_for_a_hidden_caregiver()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();
        CaregiverAvatarFixture.Seed seed = fixture.SeedCaregiver(
            status: 4,
            showProfile: false,
            avatarKey: "private/avatars/hidden.jpg");
        var storage = new RecordingStorage();

        var result = await new GetCaregiverPhotoQueryHandler(fixture.Db, storage).Handle(
            new GetCaregiverPhotoQuery(seed.CaregiverId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.Photo.NotFound", result.Error.Code);

        // A non-discoverable caregiver never reaches the storage layer.
        Assert.Null(storage.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_handler_returns_not_found_when_the_file_is_unavailable()
    {
        await using CaregiverAvatarFixture fixture = CaregiverAvatarFixture.Create();
        CaregiverAvatarFixture.Seed seed = fixture.SeedCaregiver(
            status: 4,
            showProfile: true,
            avatarKey: "private/avatars/gone.jpg");
        var storage = new RecordingStorage { Miss = true };

        var result = await new GetCaregiverPhotoQueryHandler(fixture.Db, storage).Handle(
            new GetCaregiverPhotoQuery(seed.CaregiverId),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Caregivers.Photo.NotFound", result.Error.Code);
    }

    /// <summary>
    /// Minimal SQLite database with the <c>caregivers</c> and <c>identity</c>
    /// schemas the real avatar lookup joins, seeded per-test.
    /// </summary>
    internal sealed class CaregiverAvatarFixture : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;

        private CaregiverAvatarFixture(SqliteConnection connection, CaregiversDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public CaregiversDbContext Db { get; }

        internal sealed record Seed(CaregiverId CaregiverId, Guid UserId);

        public static CaregiverAvatarFixture Create()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    """
                    ATTACH DATABASE ':memory:' AS caregivers;
                    ATTACH DATABASE ':memory:' AS identity;
                    CREATE TABLE caregivers.caregivers (
                        id TEXT NOT NULL PRIMARY KEY,
                        user_id TEXT NOT NULL,
                        type INTEGER NOT NULL,
                        status INTEGER NOT NULL,
                        show_profile INTEGER NULL
                    );
                    CREATE TABLE identity.users (
                        id TEXT NOT NULL PRIMARY KEY,
                        arabic_full_name TEXT NOT NULL,
                        english_full_name TEXT NOT NULL,
                        avatar_url TEXT NULL
                    );
                    """;
                command.ExecuteNonQuery();
            }

            var options = new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseSqlite(connection)
                .Options;

            return new CaregiverAvatarFixture(connection, new CaregiversDbContext(options));
        }

        public Seed SeedCaregiver(
            int status,
            bool showProfile,
            string? avatarKey,
            bool withIdentity = true)
        {
            Guid caregiverId = Guid.NewGuid();
            Guid userId = Guid.NewGuid();

            using (SqliteCommand command = _connection.CreateCommand())
            {
                command.CommandText =
                    "INSERT INTO caregivers.caregivers (id, user_id, type, status, show_profile) " +
                    "VALUES ($id, $userId, 1, $status, $showProfile);";
                command.Parameters.AddWithValue("$id", caregiverId);
                command.Parameters.AddWithValue("$userId", userId);
                command.Parameters.AddWithValue("$status", status);
                command.Parameters.AddWithValue("$showProfile", showProfile ? 1 : 0);
                command.ExecuteNonQuery();
            }

            if (withIdentity)
            {
                using SqliteCommand command = _connection.CreateCommand();
                command.CommandText =
                    "INSERT INTO identity.users (id, arabic_full_name, english_full_name, avatar_url) " +
                    "VALUES ($id, 'اسم', 'Name', $avatar);";
                command.Parameters.AddWithValue("$id", userId);
                command.Parameters.AddWithValue("$avatar", (object?)avatarKey ?? DBNull.Value);
                command.ExecuteNonQuery();
            }

            return new Seed(new CaregiverId(caregiverId), userId);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await _connection.DisposeAsync();
        }
    }

    /// <summary>
    /// File storage double that records the exact key it was asked to open.
    /// </summary>
    private sealed class RecordingStorage : IFileStorage
    {
        public string? LastOpenedKey { get; private set; }

        public bool Miss { get; init; }

        public Task<Result<StoredFile>> SaveAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<StoredFile>.Success(new StoredFile(folder)));

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content, string contentType, long contentLength, string folder,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<StoredFile>.Success(new StoredFile(folder)));

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            LastOpenedKey = key;
            if (Miss)
            {
                return Task.FromResult(Result<PrivateFileContent>.Failure(
                    new Error("Storage.Missing", "missing")));
            }

            return Task.FromResult(Result<PrivateFileContent>.Success(
                new PrivateFileContent(key, "image/jpeg", new MemoryStream([0xff, 0xd8, 0xff, 0xe0]))));
        }

        public Task<Result> DeleteAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
