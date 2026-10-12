using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Finance.Application;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeRoomEnrichmentTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 8, 0, 0, DateTimeKind.Utc);
    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII=");
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    // ---- domain: amenities bounds ---------------------------------------------

    [Fact]
    public void Amenities_reject_more_than_fifty_items_or_blank_language_items()
    {
        CareHomeRoomType type = NewType();

        Assert.Throws<ArgumentException>(() => type.Update("غرفة", "Room", 100m, CareHomeAllocationMode.Shared, null, null, Amenities(51), Now));
        Assert.Throws<ArgumentException>(() => type.Update("غرفة", "Room", 100m, CareHomeAllocationMode.Shared, null, null, [new BilingualCareHomeItem("", "English")], Now));
        Assert.Throws<ArgumentException>(() => type.Update("غرفة", "Room", 100m, CareHomeAllocationMode.Shared, null, null, [new BilingualCareHomeItem("عربي", "   ")], Now));
        Assert.Throws<ArgumentException>(() => type.Update("غرفة", "Room", 100m, CareHomeAllocationMode.Shared, null, null, [new BilingualCareHomeItem(new string('ع', 201), "x")], Now));
        Assert.Throws<ArgumentNullException>(() => type.Update("غرفة", "Room", 100m, CareHomeAllocationMode.Shared, null, null, null!, Now));
    }

    [Fact]
    public void Amenities_update_trims_items_and_replaces_the_previous_set()
    {
        CareHomeRoomType type = NewType();

        type.Update("غرفة", "Room", 100m, CareHomeAllocationMode.Shared, null, null, Amenities(2), Now);
        Assert.Equal(2, type.Amenities.Count);
        Assert.Equal("بند 0", type.Amenities[0].Arabic);

        type.Update("غرفة", "Room", 150m, CareHomeAllocationMode.Shared, null, null, [new BilingualCareHomeItem("  تكييف  ", "  AC ")], Now);

        Assert.Single(type.Amenities);
        Assert.Equal("تكييف", type.Amenities[0].Arabic);
        Assert.Equal("AC", type.Amenities[0].English);
        Assert.Equal(150m, type.MonthlyPriceEgp);
    }

    // ---- domain: media rules ---------------------------------------------------

    [Fact]
    public void Room_type_media_accepts_private_jpg_and_png_within_the_five_megabyte_cap()
    {
        CareHomeRoomTypeMedia cover = CareHomeRoomTypeMedia.Create(Guid.NewGuid(), CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 5_242_880, Now);
        CareHomeRoomTypeMedia gallery = CareHomeRoomTypeMedia.Create(Guid.NewGuid(), CareHomeRoomTypeMediaKind.Gallery, 9, "private/care-home-media/photo.png", "image/png", 1, Now);

        Assert.Equal(0, cover.Position);
        Assert.Equal(9, gallery.Position);
    }

    [Theory]
    [InlineData("care-home-media/photo.jpg", "image/jpeg", 100)]
    [InlineData("private/care-home-media/photo.gif", "image/gif", 100)]
    [InlineData("private/care-home-media/photo.jpg", "image/jpeg", 5_242_881)]
    public void Room_type_media_rejects_nonprivate_or_unsupported_or_oversized_images(string key, string type, long length)
    {
        Assert.Throws<DomainException>(() => CareHomeRoomTypeMedia.Create(Guid.NewGuid(), CareHomeRoomTypeMediaKind.Gallery, 0, key, type, length, Now));
    }

    [Fact]
    public void Room_type_media_rejects_wrong_kind_positions()
    {
        Assert.Throws<DomainException>(() => CareHomeRoomTypeMedia.Create(Guid.NewGuid(), CareHomeRoomTypeMediaKind.Cover, 1, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now));
        Assert.Throws<DomainException>(() => CareHomeRoomTypeMedia.Create(Guid.NewGuid(), CareHomeRoomTypeMediaKind.Gallery, 10, "private/care-home-media/photo.jpg", "image/jpeg", 10, Now));
    }

    // ---- amenities through the update handler ---------------------------------

    [Fact]
    public async Task Update_room_type_persists_amenities_and_reads_them_back()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        db.RoomTypes.Add(NewType(facility.Id));
        await db.SaveChangesAsync();

        var updated = await new UpdateRoomTypeCommandHandler(db).Handle(new UpdateRoomTypeCommand(
            owner, facility.Version, db.RoomTypes.Single().Id, "غرفة", "Room", 100m,
            CareHomeAllocationMode.Shared, null, null, Amenities(2)), default);

        Assert.True(updated.IsSuccess);
        var readback = await new GetMyInventoryQueryHandler(db).Handle(new GetMyInventoryQuery(owner), default);
        Assert.True(readback.IsSuccess);
        CareHomeRoomTypeDto dto = readback.Value.RoomTypes.Single();
        Assert.Equal(2, dto.Amenities.Count);
        Assert.Equal("بند 0", dto.Amenities[0].Arabic);
        Assert.Empty(dto.Media);
    }

    [Fact]
    public async Task Update_room_type_rejects_over_bound_amenities_with_inventory_invalid()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        db.RoomTypes.Add(NewType(facility.Id));
        await db.SaveChangesAsync();

        var rejected = await new UpdateRoomTypeCommandHandler(db).Handle(new UpdateRoomTypeCommand(
            owner, facility.Version, db.RoomTypes.Single().Id, "غرفة", "Room", 100m,
            CareHomeAllocationMode.Shared, null, null, Amenities(51)), default);

        Assert.False(rejected.IsSuccess);
        Assert.Equal("CareHomes.Inventory.Invalid", rejected.Error.Code);
    }

    // ---- media upload / remove handlers ---------------------------------------

    [Fact]
    public async Task Upload_then_remove_room_type_media_round_trips_through_private_storage()
    {
        string storageRoot = Path.Combine(Path.GetTempPath(), $"room-type-media-test-{Guid.NewGuid():N}");
        try
        {
            await using CareHomesDbContext db = CreateDb();
            UserId owner = UserId.New();
            CareHomeFacility facility = await AddFacility(db, owner);
            CareHomeRoomType type = NewType(facility.Id);
            db.RoomTypes.Add(type);
            await db.SaveChangesAsync();
            IFileStorage storage = LocalDisk(storageRoot);
            UploadRoomTypeMediaCommandHandler upload = new(db, storage);
            int version = facility.Version;

            var cover = await upload.Handle(new UploadRoomTypeMediaCommand(owner, version, type.Id, CareHomeRoomTypeMediaKind.Cover, new MemoryStream(Jpeg), "image/jpeg", Jpeg.Length), default);
            Assert.True(cover.IsSuccess);
            Assert.StartsWith("private/care-home-media/", (await db.RoomTypeMedia.SingleAsync(x => x.Id == cover.Value.Id)).StorageKey);
            Assert.NotNull(cover.Value.CoverUrl);

            var duplicateCover = await upload.Handle(new UploadRoomTypeMediaCommand(owner, version + 1, type.Id, CareHomeRoomTypeMediaKind.Cover, new MemoryStream(Jpeg), "image/jpeg", Jpeg.Length), default);
            Assert.False(duplicateCover.IsSuccess);
            Assert.Equal("CareHomes.Inventory.MediaInvalid", duplicateCover.Error.Code);

            var gallery = await upload.Handle(new UploadRoomTypeMediaCommand(owner, version + 1, type.Id, CareHomeRoomTypeMediaKind.Gallery, new MemoryStream(Png), "image/png", Png.Length), default);
            Assert.True(gallery.IsSuccess);
            Assert.Null(gallery.Value.CoverUrl);

            for (int i = 1; i < 10; i++)
            {
                var next = await upload.Handle(new UploadRoomTypeMediaCommand(owner, version + 1 + i, type.Id, CareHomeRoomTypeMediaKind.Gallery, new MemoryStream(Png), "image/png", Png.Length), default);
                Assert.True(next.IsSuccess);
            }
            var eleventh = await upload.Handle(new UploadRoomTypeMediaCommand(owner, version + 11, type.Id, CareHomeRoomTypeMediaKind.Gallery, new MemoryStream(Png), "image/png", Png.Length), default);
            Assert.False(eleventh.IsSuccess);
            Assert.Equal("CareHomes.Inventory.MediaInvalid", eleventh.Error.Code);

            var removed = await new RemoveRoomTypeMediaCommandHandler(db, storage).Handle(
                new RemoveRoomTypeMediaCommand(owner, version + 11, type.Id, cover.Value.Id), default);
            Assert.True(removed.IsSuccess);
            Assert.Empty(db.RoomTypeMedia.Where(x => x.Kind == CareHomeRoomTypeMediaKind.Cover));
            Assert.Equal(10, await db.RoomTypeMedia.CountAsync(x => x.Kind == CareHomeRoomTypeMediaKind.Gallery));

            var missing = await new RemoveRoomTypeMediaCommandHandler(db, storage).Handle(
                new RemoveRoomTypeMediaCommand(owner, version + 12, type.Id, Guid.NewGuid()), default);
            Assert.False(missing.IsSuccess);
            Assert.Equal("CareHomes.Inventory.MediaNotFound", missing.Error.Code);
        }
        finally
        {
            CleanupTemp(storageRoot);
        }
    }

    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    public async Task Upload_rejects_unsupported_content_types(string contentType)
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = NewType(facility.Id);
        db.RoomTypes.Add(type);
        await db.SaveChangesAsync();
        string storageRoot = Path.Combine(Path.GetTempPath(), $"room-type-media-test-{Guid.NewGuid():N}");
        try
        {
            IFileStorage storage = LocalDisk(storageRoot);
            var rejected = await new UploadRoomTypeMediaCommandHandler(db, storage).Handle(
                new UploadRoomTypeMediaCommand(owner, facility.Version, type.Id, CareHomeRoomTypeMediaKind.Cover, new MemoryStream(Jpeg), contentType, Jpeg.Length), default);
            Assert.False(rejected.IsSuccess);
            Assert.Equal("CareHomes.Inventory.MediaInvalid", rejected.Error.Code);
        }
        finally
        {
            CleanupTemp(storageRoot);
        }
    }

    [Fact]
    public async Task Upload_rejects_bytes_that_do_not_match_the_declared_image_signature()
    {
        await using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = NewType(facility.Id);
        db.RoomTypes.Add(type);
        await db.SaveChangesAsync();
        string storageRoot = Path.Combine(Path.GetTempPath(), $"room-type-media-test-{Guid.NewGuid():N}");
        try
        {
            IFileStorage storage = LocalDisk(storageRoot);
            var rejected = await new UploadRoomTypeMediaCommandHandler(db, storage).Handle(
                new UploadRoomTypeMediaCommand(owner, facility.Version, type.Id, CareHomeRoomTypeMediaKind.Cover, new MemoryStream(Png), "image/jpeg", Png.Length), default);
            Assert.False(rejected.IsSuccess);
            Assert.Equal("CareHomes.Inventory.MediaInvalid", rejected.Error.Code);
            Assert.Empty(await db.RoomTypeMedia.ToListAsync());
        }
        finally
        {
            CleanupTemp(storageRoot);
        }
    }

    // ---- bookability gate at hold creation ------------------------------------

    [Fact]
    public async Task Checkout_gate_blocks_holds_until_cover_and_gallery_are_published()
    {
        await using CareHomesDbContext db = CreateDb();
        CareHomeFacility facility = await AddApprovedFacility(db);
        CareHomeRoomType type = NewType(facility.Id);
        CareHomeRoom room = CareHomeRoom.Create(facility.Id, type.Id, "101", Now);
        db.RoomTypes.Add(type);
        db.Rooms.Add(room);
        db.Beds.Add(CareHomeBed.Create(facility.Id, room.Id, "A", Now));
        await db.SaveChangesAsync();
        CheckoutHandler Checkout() => new(db, new EffectiveCharges(), new EmptyCareHomeOccupancyProvider(), new InlineReservationGuard());

        // State 1: no photos at all.
        var withoutMedia = await Checkout().Handle(CheckoutCommand(facility.Id, type.Id), default);
        Assert.False(withoutMedia.IsSuccess);
        Assert.Equal("CareHomes.Inventory.RoomMediaRequired", withoutMedia.Error.Code);
        Assert.Empty(db.Bookings);

        // State 2: cover only.
        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now));
        await db.SaveChangesAsync();
        var coverOnly = await Checkout().Handle(CheckoutCommand(facility.Id, type.Id), default);
        Assert.False(coverOnly.IsSuccess);
        Assert.Equal("CareHomes.Inventory.RoomMediaRequired", coverOnly.Error.Code);

        // State 3: gallery only (cover swapped out for a gallery photo).
        db.RoomTypeMedia.Remove(db.RoomTypeMedia.Single(x => x.Kind == CareHomeRoomTypeMediaKind.Cover));
        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Gallery, 0, "private/care-home-media/photo.jpg", "image/jpeg", 10, Now));
        await db.SaveChangesAsync();
        var galleryOnly = await Checkout().Handle(CheckoutCommand(facility.Id, type.Id), default);
        Assert.False(galleryOnly.IsSuccess);
        Assert.Equal("CareHomes.Inventory.RoomMediaRequired", galleryOnly.Error.Code);

        // State 4: cover plus at least one gallery photo -> bookable.
        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now));
        await db.SaveChangesAsync();
        var published = await Checkout().Handle(CheckoutCommand(facility.Id, type.Id), default);
        Assert.True(published.IsSuccess, published.IsFailure ? $"{published.Error.Code}: {published.Error.Message}" : null);
        Assert.Single(db.Bookings);
    }

    [Theory]
    [InlineData(CareHomeRoomTypeMediaKind.Cover, 1, false)]
    [InlineData(CareHomeRoomTypeMediaKind.Gallery, 1, false)]
    [InlineData(CareHomeRoomTypeMediaKind.Cover, 2, true)]
    public void Room_type_readiness_requires_cover_and_gallery(CareHomeRoomTypeMediaKind first, int count, bool expected)
    {
        CareHomeRoomType type = NewType();
        List<CareHomeRoomTypeMedia> media = [.. Enumerable.Range(0, count)
            .Select(i => CareHomeRoomTypeMedia.Create(type.Id,
                i == 0 ? first : CareHomeRoomTypeMediaKind.Gallery,
                i == 0 && first == CareHomeRoomTypeMediaKind.Cover ? 0 : i,
                "private/care-home-media/photo.jpg", "image/jpeg", 10, Now))];

        Assert.Equal(expected, type.HasRequiredMedia(media));
    }

    // ---- SQLite twins (relational translation) --------------------------------

    [Fact]
    public async Task Room_type_amenities_translate_and_replace_on_relational_store()
    {
        using Microsoft.Data.Sqlite.SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        await using CareHomesDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        db.RoomTypes.Add(NewType(facility.Id));
        await db.SaveChangesAsync();
        Guid typeId = db.RoomTypes.Single().Id;

        var first = await new UpdateRoomTypeCommandHandler(db).Handle(new UpdateRoomTypeCommand(
            owner, facility.Version, typeId, "غرفة", "Room", 100m,
            CareHomeAllocationMode.Shared, null, null, Amenities(3)), default);
        Assert.True(first.IsSuccess);
        Assert.Equal(3, first.Value.RoomTypes.Single().Amenities.Count);

        var replaced = await new UpdateRoomTypeCommandHandler(db).Handle(new UpdateRoomTypeCommand(
            owner, first.Value.Version, typeId, "غرفة", "Room", 100m,
            CareHomeAllocationMode.Shared, null, null, [new BilingualCareHomeItem("مكيف", "AC")]), default);
        Assert.True(replaced.IsSuccess);
        Assert.Single(replaced.Value.RoomTypes.Single().Amenities);
        Assert.Equal("مكيف", replaced.Value.RoomTypes.Single().Amenities[0].Arabic);

        var readback = await new GetMyInventoryQueryHandler(db).Handle(new GetMyInventoryQuery(owner), default);
        Assert.True(readback.IsSuccess);
        Assert.Equal("AC", readback.Value.RoomTypes.Single().Amenities.Single().English);
    }

    [Fact]
    public async Task Room_type_media_persists_with_unique_kind_position_on_relational_store()
    {
        using Microsoft.Data.Sqlite.SqliteConnection connection = new("DataSource=:memory:");
        await connection.OpenAsync();
        await using CareHomesDbContext db = CreateDb(connection);
        await db.Database.EnsureCreatedAsync();
        UserId owner = UserId.New();
        CareHomeFacility facility = await AddFacility(db, owner);
        CareHomeRoomType type = NewType(facility.Id);
        db.RoomTypes.Add(type);
        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/cover.jpg", "image/jpeg", 10, Now));
        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Gallery, 0, "private/care-home-media/g1.jpg", "image/jpeg", 10, Now));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.RoomTypeMedia.CountAsync(x => x.RoomTypeId == type.Id));

        db.RoomTypeMedia.Add(CareHomeRoomTypeMedia.Create(type.Id, CareHomeRoomTypeMediaKind.Cover, 0, "private/care-home-media/dup.jpg", "image/jpeg", 10, Now));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());

        var readback = await new GetMyInventoryQueryHandler(db).Handle(new GetMyInventoryQuery(owner), default);
        Assert.True(readback.IsSuccess);
        Assert.Equal(2, readback.Value.RoomTypes.Single().Media.Count);
        Assert.NotNull(readback.Value.RoomTypes.Single().Media.Single(x => x.Kind == CareHomeRoomTypeMediaKind.Cover).CoverUrl);
        Assert.Null(readback.Value.RoomTypes.Single().Media.Single(x => x.Kind == CareHomeRoomTypeMediaKind.Gallery).CoverUrl);
    }

    // ---- helpers ---------------------------------------------------------------

    private static CareHomeRoomType NewType(CareHomeId? facilityId = null) =>
        CareHomeRoomType.Create(facilityId ?? CareHomeId.New(), "غرفة", "Room", 100m, CareHomeAllocationMode.Shared, Now);

    private static IReadOnlyList<BilingualCareHomeItem> Amenities(int count) =>
        Enumerable.Range(0, count).Select(i => new BilingualCareHomeItem($"بند {i}", $"Item {i}")).ToArray();

    private static CheckoutCareHomeBookingCommand CheckoutCommand(CareHomeId facilityId, Guid roomTypeId) =>
        new(UserId.New(), FamilyId.New(), Intake(), facilityId.Value, roomTypeId, new DateOnly(2026, 11, 1), Now);

    private static ElderlyIntakeResolution Intake() => new(
        FamilyId.New(), ElderlyId.New(), UserId.New(), "عربي", "Elderly", 75,
        new ElderlyIntakeMedicalProjection("Unknown", null, null, [], [], [], null),
        new ElderlyIntakeResponsibleContact("Contact", "+201000000000", null), null);

    private static async Task<CareHomeFacility> AddFacility(CareHomesDbContext db, UserId owner)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static async Task<CareHomeFacility> AddApprovedFacility(CareHomesDbContext db)
    {
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        foreach (CareHomeDocumentType documentType in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, documentType, $"private/{documentType}.pdf", "application/pdf", 10, null, Now);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now, new DateOnly(2026, 10, 3));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return facility;
    }

    private static CareHomesDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CareHomesDbContext CreateDb(Microsoft.Data.Sqlite.SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>().UseSqlite(connection).Options);

    private static IFileStorage LocalDisk(string storageRoot) =>
        new LocalDiskFileStorage(Options.Create(new LocalStorageOptions { RootPath = storageRoot }));

    private static void CleanupTemp(string storageRoot)
    {
        string fullTemp = Path.GetFullPath(Path.GetTempPath());
        string fullRoot = Path.GetFullPath(storageRoot);
        if (fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            Directory.Delete(fullRoot, recursive: true);
    }

    private sealed class InlineReservationGuard : ICareHomeBookingReservationGuard
    {
        public Task<T> ExecuteAsync<T>(CareHomeId facilityId, Guid roomTypeId, DateOnly startDate, Func<Task<T>> action, CancellationToken cancellationToken) => action();
    }

    private sealed class EffectiveCharges : IPlatformChargeRuleReader
    {
        public Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken) => Task.FromResult<PlatformChargeRuleRates?>(new(1m, 2m, 1));
        public Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlatformChargeRuleHistoryItem>>([]);
    }
}
