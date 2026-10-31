using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.CareHomes.Application.Inventory;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.UnitTests.CareHomes;

namespace Ahlena.UnitTests.API;

// Owner-approved decision 3a: room and room-type photo file serving.
// Every theory runs twice: InMemory + SQLite (relational translation twin —
// the serving queries touch converted CareHomeId/UserId values).
public sealed class CareHomeRoomMediaControllerTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 8, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII=");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_type_file_serves_bytes_for_approved_facility(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddApprovedFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        scope.Db.RoomTypes.Add(type);
        var media = CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomTypeMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Png, "image/png"), null);

        IActionResult result = await controller.ReadRoomTypeFile(facility.Id.Value, type.Id, media.Id, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/png", file.ContentType);
        using var body = new MemoryStream();
        await file.FileStream!.CopyToAsync(body);
        Assert.Equal(Png, body.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_type_file_404_for_unapproved_facility(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddDraftFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        scope.Db.RoomTypes.Add(type);
        var media = CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomTypeMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Png, "image/png"), null);

        IActionResult result = await controller.ReadRoomTypeFile(facility.Id.Value, type.Id, media.Id, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_type_file_404_for_wrong_facility_or_archived_type_or_unknown_media(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddApprovedFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        scope.Db.RoomTypes.Add(type);
        var media = CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomTypeMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Png, "image/png"), null);

        Assert.IsType<NotFoundResult>(await controller.ReadRoomTypeFile(Guid.NewGuid(), type.Id, media.Id, CancellationToken.None));
        Assert.IsType<NotFoundResult>(await controller.ReadRoomTypeFile(facility.Id.Value, type.Id, Guid.NewGuid(), CancellationToken.None));

        type.Archive(Now);
        await scope.Db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.ReadRoomTypeFile(facility.Id.Value, type.Id, media.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_type_file_404_when_storage_misses(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddApprovedFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        scope.Db.RoomTypes.Add(type);
        var media = CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomTypeMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Png, "image/png", miss: true), null);

        Assert.IsType<NotFoundResult>(await controller.ReadRoomTypeFile(facility.Id.Value, type.Id, media.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_room_file_serves_bytes_for_approved_facility(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddApprovedFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        scope.Db.RoomTypes.Add(type);
        scope.Db.Rooms.Add(room);
        var media = CareHomeRoomMedia.Create(room.Id, CareHomeRoomMediaKind.Cover, 0, "private/care-home-media/room.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Jpeg, "image/jpeg"), null);

        IActionResult result = await controller.ReadRoomFile(facility.Id.Value, room.Id, media.Id, CancellationToken.None);

        var file = Assert.IsType<FileStreamResult>(result);
        Assert.Equal("image/jpeg", file.ContentType);
        using var body = new MemoryStream();
        await file.FileStream!.CopyToAsync(body);
        Assert.Equal(Jpeg, body.ToArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Discovery_room_file_404_for_wrong_facility_or_archived_inventory(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddApprovedFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        scope.Db.RoomTypes.Add(type);
        scope.Db.Rooms.Add(room);
        var media = CareHomeRoomMedia.Create(room.Id, CareHomeRoomMediaKind.Cover, 0, "private/care-home-media/room.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Jpeg, "image/jpeg"), null);

        Assert.IsType<NotFoundResult>(await controller.ReadRoomFile(Guid.NewGuid(), room.Id, media.Id, CancellationToken.None));

        room.Archive(Now);
        await scope.Db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.ReadRoomFile(facility.Id.Value, room.Id, media.Id, CancellationToken.None));

        room.Restore(Now);
        type.Archive(Now);
        await scope.Db.SaveChangesAsync();
        Assert.IsType<NotFoundResult>(await controller.ReadRoomFile(facility.Id.Value, room.Id, media.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_type_file_serves_bytes_without_approval(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, UserId owner) = await AddDraftFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        scope.Db.RoomTypes.Add(type);
        var media = CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomTypeMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Png, "image/png"), owner);

        IActionResult result = await controller.ReadOwnerTypeFile(type.Id, media.Id, CancellationToken.None);

        Assert.IsType<FileStreamResult>(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_type_file_404_for_stranger_and_401_without_login(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddDraftFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        scope.Db.RoomTypes.Add(type);
        var media = CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomTypeMedia.Add(media);
        await scope.Db.SaveChangesAsync();

        var stranger = CreateController(scope.Db, new FixedStorage(Png, "image/png"), UserId.New());
        Assert.IsType<NotFoundResult>(await stranger.ReadOwnerTypeFile(type.Id, media.Id, CancellationToken.None));

        var anonymous = CreateController(scope.Db, new FixedStorage(Png, "image/png"), null);
        Assert.IsType<UnauthorizedResult>(await anonymous.ReadOwnerTypeFile(type.Id, media.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_room_file_serves_bytes_without_approval(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, UserId owner) = await AddDraftFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        scope.Db.RoomTypes.Add(type);
        scope.Db.Rooms.Add(room);
        var media = CareHomeRoomMedia.Create(room.Id, CareHomeRoomMediaKind.Cover, 0, "private/care-home-media/room.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Jpeg, "image/jpeg"), owner);

        IActionResult result = await controller.ReadOwnerRoomFile(room.Id, media.Id, CancellationToken.None);

        Assert.IsType<FileStreamResult>(result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owner_room_file_404_for_stranger(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        (CareHomeFacility facility, _) = await AddDraftFacility(scope.Db);
        CareHomeRoomType type = NewType(facility.Id);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        scope.Db.RoomTypes.Add(type);
        scope.Db.Rooms.Add(room);
        var media = CareHomeRoomMedia.Create(room.Id, CareHomeRoomMediaKind.Cover, 0, "private/care-home-media/room.jpg", "image/jpeg", 10, Now);
        scope.Db.RoomMedia.Add(media);
        await scope.Db.SaveChangesAsync();
        var controller = CreateController(scope.Db, new FixedStorage(Jpeg, "image/jpeg"), UserId.New());

        Assert.IsType<NotFoundResult>(await controller.ReadOwnerRoomFile(room.Id, media.Id, CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Upload_cover_urls_use_public_discovery_form(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        UserId owner = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        scope.Db.Facilities.Add(facility);
        await scope.Db.SaveChangesAsync();
        CareHomeRoomType type = NewType(facility.Id);
        scope.Db.RoomTypes.Add(type);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        scope.Db.Rooms.Add(room);
        await scope.Db.SaveChangesAsync();
        string storageRoot = Path.Combine(Path.GetTempPath(), $"room-media-file-test-{Guid.NewGuid():N}");
        try
        {
            IFileStorage storage = new LocalDiskFileStorage(Microsoft.Extensions.Options.Options.Create(
                new LocalStorageOptions { RootPath = storageRoot }));
            int version = facility.Version;
            var typeUpload = await new UploadRoomTypeMediaCommandHandler(scope.Db, storage).Handle(
                new UploadRoomTypeMediaCommand(owner, version, type.Id, CareHomeRoomTypeMediaKind.Cover, new MemoryStream(Jpeg), "image/jpeg", Jpeg.Length), default);
            Assert.True(typeUpload.IsSuccess);
            Assert.Equal($"/api/v1/care-homes/discovery/{facility.Id.Value:D}/room-types/{type.Id:D}/media/{typeUpload.Value.Id:D}/file",
                typeUpload.Value.CoverUrl);

            var roomUpload = await new UploadRoomMediaCommandHandler(scope.Db, storage).Handle(
                new UploadRoomMediaCommand(owner, version + 1, room.Id, CareHomeRoomMediaKind.Cover, new MemoryStream(Png), "image/png", Png.Length), default);
            Assert.True(roomUpload.IsSuccess);
            Assert.Equal($"/api/v1/care-homes/discovery/{facility.Id.Value:D}/rooms/{room.Id:D}/media/{roomUpload.Value.Id:D}/file",
                roomUpload.Value.CoverUrl);
        }
        finally
        {
            string fullTemp = Path.GetFullPath(Path.GetTempPath());
            string fullRoot = Path.GetFullPath(storageRoot);
            if (fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
                Directory.Delete(fullRoot, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Inventory_readback_cover_urls_use_public_discovery_form(bool sqlite)
    {
        await using DbScope scope = await DbScope.CreateAsync(sqlite);
        UserId owner = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        scope.Db.Facilities.Add(facility);
        await scope.Db.SaveChangesAsync();
        CareHomeRoomType type = NewType(facility.Id);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        scope.Db.RoomTypes.Add(type);
        scope.Db.Rooms.Add(room);
        scope.Db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now));
        scope.Db.RoomMedia.Add(CareHomeRoomMedia.Create(room.Id, CareHomeRoomMediaKind.Cover, 0, "private/care-home-media/room.jpg", "image/jpeg", 10, Now));
        await scope.Db.SaveChangesAsync();

        var readback = await new GetMyInventoryQueryHandler(scope.Db).Handle(new GetMyInventoryQuery(owner), default);

        Assert.True(readback.IsSuccess);
        Assert.Equal($"/api/v1/care-homes/discovery/{facility.Id.Value:D}/room-types/{type.Id:D}/media/{readback.Value.RoomTypes.Single().Media.Single().Id:D}/file",
            readback.Value.RoomTypes.Single().Media.Single().CoverUrl);
        Assert.Equal($"/api/v1/care-homes/discovery/{facility.Id.Value:D}/rooms/{room.Id:D}/media/{readback.Value.Rooms.Single().Media.Single().Id:D}/file",
            readback.Value.Rooms.Single().Media.Single().CoverUrl);
    }

    // ---- helpers ---------------------------------------------------------------

    private static CareHomeRoomType NewType(CareHomeId facilityId) =>
        CareHomeRoomType.Create(facilityId, "غرفة", "Room", 100m, CareHomeAllocationMode.Shared, Now);

    private static async Task<(CareHomeFacility Facility, UserId Owner)> AddDraftFacility(CareHomesDbContext db)
    {
        UserId owner = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return (facility, owner);
    }

    private static async Task<(CareHomeFacility Facility, UserId Owner)> AddApprovedFacility(CareHomesDbContext db)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        foreach (CareHomeDocumentType documentType in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, documentType, $"private/{documentType}.pdf", "application/pdf", 10, null, Now);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now, new DateOnly(2026, 10, 3));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return (facility, owner);
    }

    private static CareHomeRoomMediaController CreateController(CareHomesDbContext db, IFileStorage storage, UserId? subject)
    {
        var httpContext = new DefaultHttpContext();
        if (subject is not null)
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(JwtRegisteredClaimNames.Sub, subject.Value.Value.ToString())], "test"));
        return new CareHomeRoomMediaController(db, storage)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class DbScope : IAsyncDisposable
    {
        private readonly SqliteConnection? _connection;
        public CareHomesDbContext Db { get; }

        private DbScope(SqliteConnection? connection, CareHomesDbContext db)
        {
            _connection = connection;
            Db = db;
        }

        public static async Task<DbScope> CreateAsync(bool sqlite)
        {
            if (!sqlite)
                return new DbScope(null, new CareHomesDbContext(
                    new DbContextOptionsBuilder<CareHomesDbContext>()
                        .UseInMemoryDatabase(Guid.NewGuid().ToString("N")).Options));
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            return new DbScope(connection, db);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (_connection is not null) await _connection.DisposeAsync();
        }
    }

    private sealed class FixedStorage(byte[] bytes, string contentType, bool miss = false) : IFileStorage
    {
        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, long maxBytes, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            miss
                ? Task.FromResult(Result<PrivateFileContent>.Failure(new Error("Storage.Missing", "missing")))
                : Task.FromResult<Result<PrivateFileContent>>(new PrivateFileContent(key, contentType, new MemoryStream(bytes)));
        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
