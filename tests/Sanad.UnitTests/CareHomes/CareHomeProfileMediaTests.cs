using Sanad.BuildingBlocks.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeProfileMediaTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid RevisionId = Guid.Parse("0199eaf3-99b0-7c41-9490-6f77f8298d01");

    [Fact]
    public void Create_accepts_private_jpg_and_png_within_size_limit()
    {
        var cover = CareHomeProfileMedia.Create(RevisionId, CareHomeProfileMediaKind.Cover, 0,
            "private/care-home-media/cover.jpg", "image/jpeg", 5_242_880, UtcNow);
        var gallery = CareHomeProfileMedia.Create(RevisionId, CareHomeProfileMediaKind.Gallery, 9,
            "private/care-home-media/photo.png", "image/png", 1, UtcNow);

        Assert.Equal(CareHomeProfileMediaKind.Cover, cover.Kind);
        Assert.Equal(9, gallery.Position);
    }

    [Theory]
    [InlineData("care-home-media/photo.jpg", "image/jpeg", 100)]
    [InlineData("private/care-home-media/photo.gif", "image/gif", 100)]
    [InlineData("private/care-home-media/photo.jpg", "image/jpeg", 5_242_881)]
    public void Create_rejects_nonprivate_or_unsupported_or_oversized_images(string key, string type, long length)
    {
        Assert.Throws<DomainException>(() => CareHomeProfileMedia.Create(RevisionId,
            CareHomeProfileMediaKind.Gallery, 0, key, type, length, UtcNow));
    }

    [Fact]
    public async Task Upload_persists_image_through_private_storage_and_can_read_it_back()
    {
        string storageRoot = Path.Combine(Path.GetTempPath(), $"care-home-media-test-{Guid.NewGuid():N}");
        try
        {
            await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            UserId owner = UserId.New();
            var created = await new CreateCareHomeFacilityCommandHandler(db)
                .Handle(new CreateCareHomeFacilityCommand(owner), default);
            var profile = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
                new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
            var storage = new LocalDiskFileStorage(Options.Create(new LocalStorageOptions { RootPath = storageRoot }));
            var handler = new UploadCareHomeProfileMediaCommandHandler(db, storage);
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII=");

            var uploaded = await handler.Handle(new UploadCareHomeProfileMediaCommand(owner, profile.Value.Version,
                CareHomeProfileMediaKind.Gallery, new MemoryStream(png), "image/png", png.Length), default);

            Assert.True(uploaded.IsSuccess);
            var persisted = await db.ProfileMedia.SingleAsync(x => x.Id == uploaded.Value.Id);
            Assert.StartsWith("private/care-home-media/", persisted.StorageKey);
            Assert.Equal("/api/v1/care-homes/facilities/mine/media/" + persisted.Id.ToString("D") + "/file",
                uploaded.Value.FilePath);
            string relativeKey = persisted.StorageKey.Replace('/', Path.DirectorySeparatorChar);
            string storedFile = Path.Combine(storageRoot + "-private", relativeKey);
            string publicPath = Path.Combine(storageRoot, relativeKey);
            Assert.True(File.Exists(storedFile));
            Assert.False(File.Exists(publicPath));
            var readback = await storage.OpenReadAsync(persisted.StorageKey);
            Assert.True(readback.IsSuccess);
            await using var savedContent = new MemoryStream();
            await readback.Value.Content.CopyToAsync(savedContent);
            Assert.Equal(png, savedContent.ToArray());
        }
        finally
        {
            string fullTemp = Path.GetFullPath(Path.GetTempPath());
            string fullRoot = Path.GetFullPath(storageRoot);
            if (fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
                Directory.Delete(fullRoot, recursive: true);
        }
    }
}
