using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.Cms.Application.Landing;
using Ahlena.Modules.Cms.Domain.Landing;
using Ahlena.Modules.Cms.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Cms;

/// <summary>
/// E08 "delete only unreferenced landing assets": the reference-discovery safety
/// core, the fail-closed paths, and the guarantee that a refused deletion leaves
/// every row and every file untouched. Deletion runs against the real
/// <see cref="LocalDiskFileStorage"/> over a private temporary root, so
/// "the file survives" and "the file is gone" are observed on disk rather than
/// asserted against a stub.
/// </summary>
public sealed class LandingMediaDeletionTests : IDisposable
{
    private const string FreeKey = "landing/free.png";
    private const string ReferencedKey = "landing/referenced.png";

    private static readonly UserId Actor =
        new(Guid.Parse("2b5f0e08-0001-7000-8000-0000000000a1"));

    private readonly string _root =
        Path.Combine(Path.GetTempPath(), $"ahlena-e08-storage-{Guid.NewGuid():N}");

    public LandingMediaDeletionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temp folder is not a test result.
        }
    }

    [Fact]
    public async Task Unreferenced_DeletesTheFileAndTouchesNothingElse()
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(FreeKey);
        WriteFile(ReferencedKey);

        LandingSection section = DraftReferencing(ReferencedKey);
        db.LandingSections.Add(section);
        db.LandingSectionHistory.Add(LandingSectionHistory.Create(
            section.Id, LandingHistoryAction.Published, Actor, details: "text only"));
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, FreeKey);

        Assert.True(result.IsSuccess);
        Assert.False(FileExists(FreeKey));
        Assert.True(FileExists(ReferencedKey));
        Assert.Single(storage.Deleted);
        Assert.Equal(1, await db.LandingSections.CountAsync());
        Assert.Equal(1, await db.LandingSectionHistory.CountAsync());
    }

    [Theory]
    [InlineData(LandingSectionStatus.Draft)]
    [InlineData(LandingSectionStatus.Published)]
    [InlineData(LandingSectionStatus.Archived)]
    public async Task ReferencedByASectionInAnyStatus_IsRefused(LandingSectionStatus status)
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(ReferencedKey);

        LandingSection section = DraftReferencing(ReferencedKey);
        if (status == LandingSectionStatus.Published)
        {
            section.Publish();
        }
        else if (status == LandingSectionStatus.Archived)
        {
            section.Archive();
        }

        db.LandingSections.Add(section);
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, ReferencedKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaReferenced.Code, result.Error.Code);
        Assert.True(FileExists(ReferencedKey));
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task ReferencedBySectionContentJson_IsRefused()
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(ReferencedKey);

        db.LandingSections.Add(LandingSection.Create(
            LandingSectionType.Features,
            "ميزات",
            "Features",
            contentJson: $"{{\"cover\":\"{ReferencedKey}\",\"note\":\"kept\"}}"));
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, ReferencedKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaReferenced.Code, result.Error.Code);
        Assert.True(FileExists(ReferencedKey));
    }

    [Fact]
    public async Task ReferencedOnlyByRetainedLandingHistory_IsRefused()
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(ReferencedKey);

        LandingSection section = DraftReferencing(null);
        db.LandingSections.Add(section);
        db.LandingSectionHistory.Add(LandingSectionHistory.Create(
            section.Id,
            LandingHistoryAction.VisibilityChanged,
            Actor,
            details: $"hidden cover {ReferencedKey}"));
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, ReferencedKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaReferenced.Code, result.Error.Code);
        Assert.True(FileExists(ReferencedKey));
        Assert.Empty(storage.Deleted);
    }

    [Theory]
    [InlineData("imageKey")]
    [InlineData("iconKey")]
    public async Task ReferencedOnlyByFamilyHomeItems_IsRefused(string member)
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(ReferencedKey);

        db.FamilyHomeSections.Add(FamilyHomeSection.Create(
            FamilyHomeSectionKind.Banners,
            $"[{{\"englishTitle\":\"Live\",\"{member}\":\"{ReferencedKey}\"}}]"));
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, ReferencedKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaReferenced.Code, result.Error.Code);
        Assert.True(FileExists(ReferencedKey));
        Assert.Empty(storage.Deleted);
    }

    [Fact]
    public async Task ReferencedOnlyByFamilyHomeHistory_IsRefused()
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(ReferencedKey);

        FamilyHomeSection section = FamilyHomeSection.Create(FamilyHomeSectionKind.QuickLinks);
        db.FamilyHomeSections.Add(section);
        db.FamilyHomeSectionHistory.Add(FamilyHomeSectionHistory.Create(
            section.Id,
            LandingHistoryAction.Published,
            Actor,
            details: $"banner {ReferencedKey}"));
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, ReferencedKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaReferenced.Code, result.Error.Code);
        Assert.True(FileExists(ReferencedKey));
    }

    [Theory]
    [InlineData("LANDING/Referenced.PNG")]
    [InlineData("landing\\referenced.png")]
    [InlineData("/landing/referenced.png/")]
    public async Task ReferenceMatchingIsNormalizationAware(string storedValue)
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(ReferencedKey);

        db.LandingSections.Add(DraftReferencing(storedValue));
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, ReferencedKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaReferenced.Code, result.Error.Code);
        Assert.True(FileExists(ReferencedKey));
    }

    [Fact]
    public async Task UnreadableFamilyHomePayload_FailsClosed()
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(FreeKey);

        db.FamilyHomeSections.Add(FamilyHomeSection.Create(
            FamilyHomeSectionKind.Banners, "{ this is not readable json"));
        await db.SaveChangesAsync();

        Result result = await Handle(db, storage, FreeKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaReferenced.Code, result.Error.Code);
        Assert.True(FileExists(FreeKey));
        Assert.Empty(storage.Deleted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("private/landing/free.png")]
    [InlineData("documents/free.png")]
    [InlineData("landing")]
    [InlineData("landing/../free.png")]
    [InlineData("landing\\..\\free.png")]
    [InlineData("landing/..%2ffree.png")]
    public async Task InvalidKeys_AreRefusedBeforeTheStorageInterfaceIsReached(string? key)
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(FreeKey);

        Result result = await Handle(db, storage, key);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaKeyInvalid.Code, result.Error.Code);
        Assert.Empty(storage.Deleted);
        Assert.True(FileExists(FreeKey));
    }

    [Fact]
    public async Task PrivatePrefixIsRefusedEvenWhenThePathWouldResolve()
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        const string privateKey = "private/landing/free.png";
        WriteFile(privateKey);

        Result result = await Handle(db, storage, privateKey);

        Assert.True(result.IsFailure);
        Assert.Equal(LandingErrors.MediaKeyInvalid.Code, result.Error.Code);
        Assert.Empty(storage.Deleted);
        Assert.True(FileExists(privateKey));
    }

    [Fact]
    public async Task DeletingOneAsset_LeavesEveryOtherFileUntouched()
    {
        await using CmsDbContext db = CreateDb();
        CountingFileStorage storage = CreateStorage();
        WriteFile(FreeKey);
        WriteFile("landing/sibling.png");
        WriteFile("landing/nested/other.png");

        byte[] sibling = File.ReadAllBytes(FullPath("landing/sibling.png"));

        Result result = await Handle(db, storage, FreeKey);

        Assert.True(result.IsSuccess);
        Assert.False(FileExists(FreeKey));
        Assert.True(FileExists("landing/sibling.png"));
        Assert.True(FileExists("landing/nested/other.png"));
        Assert.Equal(sibling, File.ReadAllBytes(FullPath("landing/sibling.png")));
    }

    private static Task<Result> Handle(CmsDbContext db, IFileStorage storage, string? key) =>
        new DeleteLandingMediaCommandHandler(db, storage)
            .Handle(new DeleteLandingMediaCommand(key), CancellationToken.None);

    private static LandingSection DraftReferencing(string? imagePath) =>
        LandingSection.Create(
            LandingSectionType.Hero,
            "بطل",
            "Hero",
            imagePath: imagePath);

    private static CmsDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new CmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private CountingFileStorage CreateStorage() =>
        new(new LocalDiskFileStorage(Options.Create(new LocalStorageOptions { RootPath = _root })));

    private string FullPath(string key) =>
        Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar));

    private void WriteFile(string key)
    {
        string path = FullPath(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
    }

    private bool FileExists(string key) => File.Exists(FullPath(key));

    /// <summary>
    /// Records every key handed to the storage interface while performing the real
    /// deletion, so a refused request can be proven never to have reached it.
    /// </summary>
    private sealed class CountingFileStorage(IFileStorage inner) : IFileStorage
    {
        public List<string> Deleted { get; } = [];

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Deleted.Add(key);
            return inner.DeleteAsync(key, cancellationToken);
        }

        public Task<Result<StoredFile>> SaveAsync(
            Stream content,
            string contentType,
            long contentLength,
            string folder,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(content, contentType, contentLength, folder, cancellationToken);

        public Task<Result<StoredFile>> SavePrivateAsync(
            Stream content,
            string contentType,
            long contentLength,
            string folder,
            CancellationToken cancellationToken = default) =>
            inner.SavePrivateAsync(content, contentType, contentLength, folder, cancellationToken);

        public Task<Result<PrivateFileContent>> OpenReadAsync(
            string key,
            CancellationToken cancellationToken = default) =>
            inner.OpenReadAsync(key, cancellationToken);
    }
}
