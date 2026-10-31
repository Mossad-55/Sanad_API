using Ahlena.BuildingBlocks.Domain.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.CareHomes.Application.Facilities;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;

namespace Ahlena.UnitTests.CareHomes;

public sealed class CareHomeProfileMediaTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 8);
    private static readonly Guid RevisionId = Guid.Parse("0199eaf3-99b0-7c41-9490-6f77f8298d01");

    [Fact]
    public void Clone_preserves_media_fields_and_storage_reference()
    {
        var source = CareHomeProfileMedia.Create(RevisionId, CareHomeProfileMediaKind.Gallery, 2,
            "private/care-home-media/photo.png", "image/png", 10, UtcNow);
        var targetRevisionId = Guid.Parse("0199eaf3-99b0-7c41-9490-6f77f8298d02");

        var clone = CareHomeProfileMedia.Clone(source, targetRevisionId);

        Assert.NotEqual(source.Id, clone.Id);
        Assert.Equal(targetRevisionId, clone.ProfileRevisionId);
        Assert.Equal(source.Kind, clone.Kind);
        Assert.Equal(source.Position, clone.Position);
        Assert.Equal(source.StorageKey, clone.StorageKey);
        Assert.Equal(source.ContentType, clone.ContentType);
        Assert.Equal(source.Length, clone.Length);
    }

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
            var created = await new CreateCareHomeFacilityCommandHandler(db, CareHomeTestClockHost.Clock())
                .Handle(new CreateCareHomeFacilityCommand(owner), default);
            var profile = await new SaveMyCareHomeProfileCommandHandler(db, CareHomeTestClockHost.Clock()).Handle(
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

    [Fact]
    public async Task Submit_after_removing_inherited_media_keeps_the_approved_file_and_does_not_restore_the_asset()
    {
        string storageRoot = Path.Combine(Path.GetTempPath(), $"care-home-media-carry-{Guid.NewGuid():N}");
        try
        {
            await using var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
            UserId owner = UserId.New();
            UserId admin = UserId.New();
            var storage = new LocalDiskFileStorage(
                Options.Create(new LocalStorageOptions { RootPath = storageRoot }));
            byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII=");

            var created = await new CreateCareHomeFacilityCommandHandler(db, CareHomeTestClockHost.Clock())
                .Handle(new CreateCareHomeFacilityCommand(owner), default);
            var saved = await new SaveMyCareHomeProfileCommandHandler(db, CareHomeTestClockHost.Clock()).Handle(
                new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
            Assert.True(saved.IsSuccess);
            CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents).SingleAsync();
            foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            {
                CareHomeDocument document = facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, UtcNow);
                document.Verify(admin, null, true, UtcNow);
            }
            await db.SaveChangesAsync();

            var upload = new UploadCareHomeProfileMediaCommandHandler(db, storage);
            var cover = await upload.Handle(new UploadCareHomeProfileMediaCommand(owner,
                facility.Version, CareHomeProfileMediaKind.Cover, new MemoryStream(png), "image/png", png.Length), default);
            Assert.True(cover.IsSuccess);
            var gallery = await upload.Handle(new UploadCareHomeProfileMediaCommand(owner,
                facility.Version, CareHomeProfileMediaKind.Gallery, new MemoryStream(png), "image/png", png.Length), default);
            Assert.True(gallery.IsSuccess);

            var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db, CareHomeTestClockHost.Clock()).Handle(
                new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
            Assert.True(submitted.IsSuccess);
            facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, UtcNow, Today);
            await db.SaveChangesAsync();
            Guid approvedRevisionId = facility.ApprovedRevisionId!.Value;

            CareHomeProfileMedia approvedGallery = await db.ProfileMedia.AsNoTracking().SingleAsync(x =>
                x.ProfileRevisionId == approvedRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery);
            string approvedGalleryPath = PrivateFilePath(storageRoot, approvedGallery.StorageKey);
            Assert.True(File.Exists(approvedGalleryPath));

            var edit = await new SaveMyCareHomeProfileCommandHandler(db, CareHomeTestClockHost.Clock()).Handle(
                new SaveMyCareHomeProfileCommand(owner, facility.Version,
                    CareHomeFacilityTests.Draft() with { ContactPhone = "+201999999999" }), default);
            Assert.True(edit.IsSuccess);
            Guid openRevisionId = edit.Value.OpenRevisionId!.Value;
            Assert.NotEqual(approvedRevisionId, openRevisionId);

            CareHomeProfileMedia inheritedGallery = await db.ProfileMedia.AsNoTracking().SingleAsync(x =>
                x.ProfileRevisionId == openRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery);
            Assert.Equal(approvedGallery.StorageKey, inheritedGallery.StorageKey);

            var remove = await new RemoveCareHomeProfileMediaCommandHandler(db, storage).Handle(
                new RemoveCareHomeProfileMediaCommand(owner, facility.Version, inheritedGallery.Id), default);
            Assert.True(remove.IsSuccess);

            // Only the editable draft row is gone; the approved revision still references the
            // same private file, so the shared file survives and stays readable.
            Assert.False(await db.ProfileMedia.AnyAsync(x => x.Id == inheritedGallery.Id));
            Assert.True(await db.ProfileMedia.AnyAsync(x => x.Id == approvedGallery.Id));
            Assert.True(File.Exists(approvedGalleryPath));
            var readback = await storage.OpenReadAsync(approvedGallery.StorageKey);
            Assert.True(readback.IsSuccess);
            await readback.Value.Content.DisposeAsync();

            // Re-saving the draft must not resurrect the deliberately removed asset.
            var resave = await new SaveMyCareHomeProfileCommandHandler(db, CareHomeTestClockHost.Clock()).Handle(
                new SaveMyCareHomeProfileCommand(owner, facility.Version,
                    CareHomeFacilityTests.Draft() with { ContactPhone = "+201888888888" }), default);
            Assert.True(resave.IsSuccess);
            Assert.Equal(openRevisionId, resave.Value.OpenRevisionId!.Value);
            Assert.False(await db.ProfileMedia.AnyAsync(x =>
                x.ProfileRevisionId == openRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery));

            // Submission never silently restores the removed asset: with only the cover left
            // the two-photo rule still refuses, and it stays refused until a real replacement
            // is uploaded through the normal flow.
            var submittedWithoutReplacement = await new SubmitMyCareHomeApplicationCommandHandler(db, CareHomeTestClockHost.Clock()).Handle(
                new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
            Assert.False(submittedWithoutReplacement.IsSuccess);
            Assert.Equal("CareHomes.Facility.ProfileMediaRequired", submittedWithoutReplacement.Error.Code);

            var replacement = await upload.Handle(new UploadCareHomeProfileMediaCommand(owner,
                facility.Version, CareHomeProfileMediaKind.Gallery, new MemoryStream(png), "image/png", png.Length), default);
            Assert.True(replacement.IsSuccess);
            Assert.NotEqual(inheritedGallery.Id, replacement.Value.Id);

            var submittedAfterReplacement = await new SubmitMyCareHomeApplicationCommandHandler(db, CareHomeTestClockHost.Clock()).Handle(
                new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
            Assert.True(submittedAfterReplacement.IsSuccess);

            db.ChangeTracker.Clear();
            Assert.False(await db.ProfileMedia.AnyAsync(x => x.Id == inheritedGallery.Id));
            Assert.Equal(1, await db.ProfileMedia.CountAsync(x =>
                x.ProfileRevisionId == openRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery));
            Assert.True(await db.ProfileMedia.AnyAsync(x =>
                x.ProfileRevisionId == approvedRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery));
            CareHomeFacility reloaded = await db.Facilities.SingleAsync();
            Assert.Equal(approvedRevisionId, reloaded.ApprovedRevisionId);
            Assert.Equal(openRevisionId, reloaded.SubmittedRevisionId);
        }
        finally
        {
            DeleteStorageRoot(storageRoot);
        }
    }

    private static string PrivateFilePath(string storageRoot, string storageKey)
    {
        string root = Path.GetFullPath(storageRoot).TrimEnd(
            Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + "-private";
        return Path.Combine(root, storageKey.Replace('/', Path.DirectorySeparatorChar));
    }

    private static void DeleteStorageRoot(string storageRoot)
    {
        string fullTemp = Path.GetFullPath(Path.GetTempPath());
        string fullRoot = Path.GetFullPath(storageRoot);
        if (fullRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(fullRoot))
            Directory.Delete(fullRoot, recursive: true);
        string privateRoot = fullRoot + "-private";
        if (privateRoot.StartsWith(fullTemp, StringComparison.OrdinalIgnoreCase) && Directory.Exists(privateRoot))
            Directory.Delete(privateRoot, recursive: true);
    }
}
