using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeProfileCommandTests
{
    [Fact]
    public async Task CreateFacility_IsOnePerOwner_AndGetMineDoesNotExposeOtherOwners()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        var handler = new CreateCareHomeFacilityCommandHandler(db);

        var created = await handler.Handle(new CreateCareHomeFacilityCommand(owner), default);
        var duplicate = await handler.Handle(new CreateCareHomeFacilityCommand(owner), default);
        var otherOwnerRead = await new GetMyCareHomeFacilityQueryHandler(db)
            .Handle(new GetMyCareHomeFacilityQuery(UserId.New()), default);

        Assert.True(created.IsSuccess);
        Assert.Equal(CareHomeStatus.Draft, created.Value.Status);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("CareHomes.Facility.AlreadyExists", duplicate.Error.Code);
        Assert.False(otherOwnerRead.IsSuccess);
        Assert.Equal("CareHomes.Facility.NotFound", otherOwnerRead.Error.Code);
        Assert.Equal(1, await db.Facilities.CountAsync());
    }

    [Fact]
    public async Task SaveProfile_UsesVersionAndPersistsBilingualDraft()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var handler = new SaveMyCareHomeProfileCommandHandler(db);
        CareHomeProfileDraft draft = CareHomeFacilityTests.Draft();

        var saved = await handler.Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, draft), default);
        var stale = await handler.Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, draft), default);

        Assert.True(saved.IsSuccess);
        Assert.Equal(draft.ArabicName, saved.Value.Draft!.ArabicName);
        Assert.Equal(created.Value.Version + 1, saved.Value.Version);
        Assert.False(stale.IsSuccess);
        Assert.Equal("CareHomes.Facility.Conflict", stale.Error.Code);
        db.ChangeTracker.Clear();
        CareHomeFacility persisted = await db.Facilities.Include(x => x.Revisions).SingleAsync();
        Assert.Equal(draft.EnglishName, persisted.Revisions.Single().EnglishName);
    }

    [Fact]
    public async Task Submit_RequiresDocuments_ThenFreezesBilingualRevision()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        var handler = new SubmitMyCareHomeApplicationCommandHandler(db);

        var missingEverything = await handler.Handle(
            new SubmitMyCareHomeApplicationCommand(owner, saved.Value.Version), default);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).SingleAsync();
        db.ProfileMedia.Add(CoverMedia(facility));
        db.ProfileMedia.Add(GalleryMedia(facility, 0));
        await db.SaveChangesAsync();
        var missingDocuments = await handler.Handle(
            new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var submitted = await handler.Handle(
            new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);

        Assert.False(missingEverything.IsSuccess);
        Assert.Equal("CareHomes.Facility.ProfileMediaRequired", missingEverything.Error.Code);
        Assert.False(missingDocuments.IsSuccess);
        Assert.Equal("CareHomes.Facility.InvalidSubmission", missingDocuments.Error.Code);
        Assert.True(submitted.IsSuccess);
        Assert.Equal(CareHomeStatus.PendingReview, submitted.Value.Status);
        Assert.NotNull(submitted.Value.SubmittedRevisionId);
        Assert.True(facility.Revisions.Single().IsFrozen);
    }

    [Fact]
    public async Task Submit_RequiresCoverPhoto_EvenWithTwoGalleryPhotos()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        var handler = new SubmitMyCareHomeApplicationCommandHandler(db);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).SingleAsync();
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ProfileMedia.Add(GalleryMedia(facility, 0));
        db.ProfileMedia.Add(GalleryMedia(facility, 1));
        await db.SaveChangesAsync();

        var galleryOnly = await handler.Handle(
            new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);

        Assert.False(galleryOnly.IsSuccess);
        Assert.Equal("CareHomes.Facility.ProfileMediaRequired", galleryOnly.Error.Code);

        db.ProfileMedia.Add(CoverMedia(facility));
        await db.SaveChangesAsync();
        var submitted = await handler.Handle(
            new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);

        Assert.True(submitted.IsSuccess);
        Assert.Equal(CareHomeStatus.PendingReview, submitted.Value.Status);
    }

    [Fact]
    public async Task Submit_WithCoverAndGallery_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CareHomesDbContext(
            new DbContextOptionsBuilder<CareHomesDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        UserId owner = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        Assert.True(saved.IsSuccess);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).SingleAsync();
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        db.ProfileMedia.Add(CoverMedia(facility));
        db.ProfileMedia.Add(GalleryMedia(facility, 0));
        await db.SaveChangesAsync();

        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db)
            .Handle(new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);

        Assert.True(submitted.IsSuccess);
        Assert.Equal(CareHomeStatus.PendingReview, submitted.Value.Status);
    }

    private static CareHomeProfileMedia CoverMedia(CareHomeFacility facility) =>
        CareHomeProfileMedia.Create(
            facility.Revisions.Single().Id, CareHomeProfileMediaKind.Cover, 0,
            "private/care-home-media/cover.jpg", "image/jpeg", 100, DateTime.UtcNow);

    private static CareHomeProfileMedia GalleryMedia(CareHomeFacility facility, int position) =>
        CareHomeProfileMedia.Create(
            facility.Revisions.Single().Id, CareHomeProfileMediaKind.Gallery, position,
            $"private/care-home-media/gallery-{position}.jpg", "image/jpeg", 100, DateTime.UtcNow);

    private static CareHomesDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
