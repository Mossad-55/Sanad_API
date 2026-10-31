using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Community.Application.Abstractions.Data;
using Ahlena.Modules.Community.Application.Posts;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Community;

/// <summary>
/// S3 approved photo route (refund decision lock #32) for a Community author's
/// avatar. The route streams the image bytes for a real, non-anonymous author
/// and answers a generic 404 for an unknown author, an author without a stored
/// avatar and an anonymous author — resolving anonymity server-side from the
/// stored <c>is_anonymous</c> flag, never from a caller-supplied hint. The
/// stored Identity private avatar key is never returned on the wire.
/// </summary>
public sealed class CommunityAuthorPhotoTests
{
    [Fact]
    public async Task Photo_streams_the_stored_avatar_for_a_named_author()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var author = UserId.New();
        await harness.AddIdentityUserAsync(author.Value, "الكاتب", "Named Author", "private/avatars/named.jpg");
        await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);

        var result = await harness.ReadAsync(author);

        Assert.True(result.IsSuccess);
        Assert.Equal("image/jpeg", result.Value.ContentType);
        Assert.Equal(harness.AvatarBytes, ReadAll(result.Value.Content));

        // The key is consumed server-side only; the handler returns bytes.
        Assert.Equal("private/avatars/named.jpg", harness.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_is_not_found_for_an_anonymous_author()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var hidden = UserId.New();
        await harness.AddIdentityUserAsync(hidden.Value, "المخفي", "Hidden Author", "private/avatars/hidden.jpg");
        await harness.AddPublishedPostAsync(hidden.Value, isAnonymous: true);

        var result = await harness.ReadAsync(hidden);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityAuthorPhotoErrors.NotFound, result.Error);

        // The anonymity decision never even reads the stored avatar file.
        Assert.Null(harness.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_is_not_found_when_the_author_wrote_anonymously_among_named_posts()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var author = UserId.New();
        await harness.AddIdentityUserAsync(author.Value, "الكاتب", "Mixed Author", "private/avatars/mixed.jpg");
        await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);
        await harness.AddPublishedPostAsync(author.Value, isAnonymous: true);

        var result = await harness.ReadAsync(author);

        // One anonymous item is enough to withhold the identifying avatar.
        Assert.True(result.IsFailure);
        Assert.Equal(CommunityAuthorPhotoErrors.NotFound, result.Error);
        Assert.Null(harness.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_is_not_found_for_an_author_without_a_stored_avatar()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var author = UserId.New();
        await harness.AddIdentityUserAsync(author.Value, "بلا صورة", "No Avatar", avatarKey: null);
        await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);

        var result = await harness.ReadAsync(author);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityAuthorPhotoErrors.NotFound, result.Error);
        Assert.Null(harness.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_is_not_found_for_an_identity_user_that_never_authored_a_published_post()
    {
        // A real identity row with a stored avatar but zero published Community
        // activity is not a "Community author": the route serves nothing so an
        // arbitrary identity's avatar is never streamable by any Normal JWT.
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var outsider = UserId.New();
        await harness.AddIdentityUserAsync(outsider.Value, "غريب", "Outsider", "private/avatars/outsider.jpg");

        var result = await harness.ReadAsync(outsider);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityAuthorPhotoErrors.NotFound, result.Error);
        Assert.Null(harness.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_is_not_found_for_an_unknown_author()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();

        var result = await harness.ReadAsync(UserId.New());

        // Unknown, no-avatar and anonymous are the same generic 404.
        Assert.True(result.IsFailure);
        Assert.Equal(CommunityAuthorPhotoErrors.NotFound, result.Error);
        Assert.Null(harness.LastOpenedKey);
    }

    [Fact]
    public async Task Photo_is_not_found_when_the_stored_file_cannot_be_read()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var author = UserId.New();
        await harness.AddIdentityUserAsync(author.Value, "الكاتب", "Broken File", "private/avatars/missing.jpg");
        await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);
        harness.StorageMiss = true;

        var result = await harness.ReadAsync(author);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityAuthorPhotoErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Photo_route_is_published_as_a_download_path_and_never_a_raw_key()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var author = UserId.New();
        await harness.AddIdentityUserAsync(author.Value, "الكاتب", "Named Author", "private/avatars/named.jpg");
        await harness.AddPublishedPostAsync(author.Value, isAnonymous: false);

        IReadOnlyDictionary<Guid, CommunityAuthorProfile> profiles =
            (await new GetCommunityAuthorProfilesQueryHandler(harness.Context).Handle(
                new GetCommunityAuthorProfilesQuery([author.Value]),
                CancellationToken.None)).Value;

        CommunityAuthorProfile profile = profiles[author.Value];
        Assert.Equal("Named Author", profile.DisplayName);
        Assert.Equal($"/api/v1/community/authors/{author.Value}/photo", profile.AvatarUrl);

        // The published avatar is a path the route resolves; the private key
        // never leaves the server.
        Assert.DoesNotContain("private/avatars", profile.AvatarUrl!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Anonymous_author_is_not_served_and_no_author_identity_leaks()
    {
        await using CommunityAuthorPhotoHarness harness = await CommunityAuthorPhotoHarness.CreateAsync();
        var hidden = UserId.New();
        await harness.AddIdentityUserAsync(hidden.Value, "المخفي", "Hidden Author", "private/avatars/hidden.jpg");
        Guid anonymousPost = await harness.AddPublishedPostAsync(hidden.Value, isAnonymous: true);

        // The feed projects no identity for an anonymous author, so the only
        // author id the route can be called with is the one the feed withheld.
        // Withholding the avatar is the point: the handler answers 404 and
        // never touches the stored file.
        var result = await harness.ReadAsync(hidden);

        Assert.True(result.IsFailure);
        Assert.Equal(CommunityAuthorPhotoErrors.NotFound, result.Error);
        Assert.Null(harness.LastOpenedKey);
        Assert.NotEqual(default, anonymousPost);
    }

    private static byte[] ReadAll(Stream stream)
    {
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}

/// <summary>
/// SQLite Community database that also carries a minimal Identity <c>users</c>
/// table (with the <c>avatar_url</c> column the author lookup reads), wired to
/// a recording file storage so the tests can assert which private key — if any
/// — the handler opened.
/// </summary>
internal sealed class CommunityAuthorPhotoHarness : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly CommunityDbContext _inner;

    private CommunityAuthorPhotoHarness(SqliteConnection connection, CommunityDbContext inner)
    {
        _connection = connection;
        _inner = inner;
        Context = inner;
        Storage = new RecordingFileStorage();
    }

    public ICommunityDbContext Context { get; }

    public RecordingFileStorage Storage { get; }

    public bool StorageMiss
    {
        get => Storage.Miss;
        set => Storage.Miss = value;
    }

    public byte[] AvatarBytes { get; } = [0xff, 0xd8, 0xff, 0xe0];

    public string? LastOpenedKey => Storage.LastOpenedKey;

    public static async Task<CommunityAuthorPhotoHarness> CreateAsync()
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();

        var inner = new CommunityDbContext(
            new DbContextOptionsBuilder<CommunityDbContext>().UseSqlite(connection).Options);
        await inner.Database.EnsureCreatedAsync();

        await inner.Database.ExecuteSqlRawAsync(
            "CREATE TABLE users (id TEXT NOT NULL PRIMARY KEY, " +
            "arabic_full_name TEXT NOT NULL, english_full_name TEXT NOT NULL, " +
            "avatar_url TEXT NULL);");

        return new CommunityAuthorPhotoHarness(connection, inner);
    }

    public Task AddIdentityUserAsync(Guid userId, string arabicName, string englishName, string? avatarKey)
    {
        // Raw ADO rather than EF raw SQL: a null avatar binds as a plain
        // DBNull through SqliteParameter, which EF's standalone raw-SQL
        // parameter mapping cannot resolve a type mapping for.
        SqliteCommand command = _connection.CreateCommand();
        command.CommandText =
            "INSERT INTO users (id, arabic_full_name, english_full_name, avatar_url) " +
            "VALUES ($id, $arabicName, $englishName, $avatarKey);";
        // The id binds the same way the product binds its Guid identity
        // lookups (DbType.Guid), so the seeded value matches byte-for-byte.
        command.Parameters.AddWithValue("$id", userId);
        command.Parameters.AddWithValue("$arabicName", arabicName);
        command.Parameters.AddWithValue("$englishName", englishName);
        command.Parameters.AddWithValue("$avatarKey", (object?)avatarKey ?? DBNull.Value);
        return command.ExecuteNonQueryAsync();
    }

    public async Task<Guid> AddPublishedPostAsync(Guid authorId, bool isAnonymous)
    {
        DateTime stamp = DateTime.UtcNow;
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, isAnonymous,
            new UserId(authorId), stamp);
        post.Publish(new UserId(authorId), stamp);
        _inner.Posts.Add(post);
        await _inner.SaveChangesAsync();
        return post.Id.Value;
    }

    public Task<Result<CommunityAuthorPhotoResponse>> ReadAsync(UserId author) =>
        new GetCommunityAuthorPhotoQueryHandler(Context, Storage)
            .Handle(new GetCommunityAuthorPhotoQuery(author), CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();
        await _connection.DisposeAsync();
    }
}

/// <summary>
/// File storage double that records the exact key it was asked to open, so a
/// test can prove whether the avatar key was read at all.
/// </summary>
internal sealed class RecordingFileStorage : IFileStorage
{
    public string? LastOpenedKey { get; private set; }

    public bool Miss { get; set; }

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
