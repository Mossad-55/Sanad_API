using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
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

    [Fact]
    public async Task ApprovedFacility_PhoneOnlyEdit_CarriesForwardDocsAndMedia_AndSubmitSucceeds()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents).SingleAsync();
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            CareHomeDocument document = facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, new DateOnly(2027, 1, 1), DateTime.UtcNow);
            document.Verify(admin, document.ExpiryDate, false, DateTime.UtcNow);
        }
        db.ProfileMedia.Add(CoverMedia(facility));
        db.ProfileMedia.Add(GalleryMedia(facility, 0));
        await db.SaveChangesAsync();
        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db)
            .Handle(new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
        Assert.True(submitted.IsSuccess);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, DateTime.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow));
        await db.SaveChangesAsync();
        Assert.Equal(CareHomeStatus.Approved, facility.Status);
        Guid approvedRevisionId = facility.ApprovedRevisionId!.Value;

        var approvedDocs = await db.Facilities.AsNoTracking().SelectMany(x => x.Documents)
            .Where(x => x.ProfileRevisionId == approvedRevisionId).ToListAsync();
        var approvedMedia = await db.ProfileMedia.AsNoTracking()
            .Where(x => x.ProfileRevisionId == approvedRevisionId).OrderBy(x => x.Position).ToListAsync();

        CareHomeProfileDraft edited = CareHomeFacilityTests.Draft() with { ContactPhone = "+201999999999" };
        var edit = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version, edited), default);
        Assert.True(edit.IsSuccess);
        Assert.NotNull(edit.Value.OpenRevisionId);
        Assert.NotEqual(approvedRevisionId, edit.Value.OpenRevisionId);

        var inheritedDocs = await db.Facilities.AsNoTracking().SelectMany(x => x.Documents)
            .Where(x => x.ProfileRevisionId == edit.Value.OpenRevisionId).ToListAsync();
        Assert.Equal(4, inheritedDocs.Count);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            CareHomeDocument original = approvedDocs.Single(x => x.Type == type);
            CareHomeDocument clone = inheritedDocs.Single(x => x.Type == type);
            Assert.Equal(original.PrivateStorageKey, clone.PrivateStorageKey);
            Assert.Equal(original.Status, clone.Status);
            Assert.Equal(original.ExpiryDate, clone.ExpiryDate);
            Assert.Equal(original.VerifiedByUserId, clone.VerifiedByUserId);
            Assert.Equal(original.VerifiedOnUtc, clone.VerifiedOnUtc);
        }

        var inheritedMedia = await db.ProfileMedia.AsNoTracking()
            .Where(x => x.ProfileRevisionId == edit.Value.OpenRevisionId).OrderBy(x => x.Position).ToListAsync();
        Assert.Equal(approvedMedia.Count, inheritedMedia.Count);
        foreach (var original in approvedMedia)
        {
            var inherited = Assert.Single(inheritedMedia, x =>
                x.Kind == original.Kind && x.Position == original.Position);
            Assert.Equal(original.StorageKey, inherited.StorageKey);
        }

        var submittedEdit = await new SubmitMyCareHomeApplicationCommandHandler(db)
            .Handle(new SubmitMyCareHomeApplicationCommand(owner, edit.Value.Version), default);
        Assert.True(submittedEdit.IsSuccess);

        db.ChangeTracker.Clear();
        CareHomeFacility reloadedApproved = await db.Facilities.Include(x => x.Revisions).SingleAsync();
        Assert.Equal(approvedRevisionId, reloadedApproved.ApprovedRevisionId);
    }

    [Fact]
    public async Task ApprovedFacility_MediaFirstGalleryUpload_InheritsCountAndPosition()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CareHomesDbContext(
            new DbContextOptionsBuilder<CareHomesDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents).SingleAsync();
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            CareHomeDocument document = facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, DateTime.UtcNow);
            document.Verify(admin, null, true, DateTime.UtcNow);
        }
        db.ProfileMedia.Add(CoverMedia(facility));
        db.ProfileMedia.Add(GalleryMedia(facility, 0));
        await db.SaveChangesAsync();
        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db)
            .Handle(new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
        Assert.True(submitted.IsSuccess);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, DateTime.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow));
        await db.SaveChangesAsync();

        byte[] png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/F5sAAAAASUVORK5CYII=");
        int expectedVersion = facility.Version;
        db.ChangeTracker.Clear();
        var storage = new RecordingStorage();
        var handler = new UploadCareHomeProfileMediaCommandHandler(db, storage);
        var uploaded = await handler.Handle(new UploadCareHomeProfileMediaCommand(owner, expectedVersion, CareHomeProfileMediaKind.Gallery, new MemoryStream(png), "image/png", png.Length), default);
        Assert.True(uploaded.IsSuccess);
        Assert.Equal(1, uploaded.Value.Position);

        db.ChangeTracker.Clear();
        var inheritedGallery = await db.ProfileMedia.AsNoTracking()
            .Where(x => x.ProfileRevisionId == uploaded.Value.ProfileRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery).ToListAsync();
        Assert.Equal(2, inheritedGallery.Count);
        Assert.Equal(new[] { 0, 1 }, inheritedGallery.Select(x => x.Position).OrderBy(x => x));
        Assert.Equal(1, await db.ProfileMedia.CountAsync(x =>
            x.ProfileRevisionId == uploaded.Value.ProfileRevisionId && x.Kind == CareHomeProfileMediaKind.Cover));
        var reloaded = await db.Facilities.Include(x => x.Documents).SingleAsync();
        Assert.Equal(4, reloaded.Documents.Count(x => x.ProfileRevisionId == uploaded.Value.ProfileRevisionId));
    }

    [Fact]
    public async Task RemovingInheritedDraftMedia_DoesNotDeleteSharedFileOrResurrectOnResave()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents).SingleAsync();
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            CareHomeDocument document = facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, DateTime.UtcNow);
            document.Verify(admin, null, true, DateTime.UtcNow);
        }
        db.ProfileMedia.Add(CoverMedia(facility));
        db.ProfileMedia.Add(GalleryMedia(facility, 0));
        await db.SaveChangesAsync();
        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db)
            .Handle(new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
        Assert.True(submitted.IsSuccess);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, DateTime.UtcNow, DateOnly.FromDateTime(DateTime.UtcNow));
        await db.SaveChangesAsync();

        var edit = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version, CareHomeFacilityTests.Draft() with { ContactPhone = "+201999999999" }), default);
        Assert.True(edit.IsSuccess);
        Guid inheritedMediaId = (await db.ProfileMedia.FirstAsync(x => x.ProfileRevisionId == edit.Value.OpenRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery)).Id;
        var storage = new RecordingStorage();
        var remove = await new RemoveCareHomeProfileMediaCommandHandler(db, storage).Handle(
            new RemoveCareHomeProfileMediaCommand(owner, edit.Value.Version, inheritedMediaId), default);
        Assert.True(remove.IsSuccess);
        Assert.Empty(storage.DeletedKeys);
        Assert.False(await db.ProfileMedia.AnyAsync(x => x.Id == inheritedMediaId));

        int currentVersion = (await db.Facilities.SingleAsync()).Version;
        var resave = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, currentVersion, CareHomeFacilityTests.Draft() with { ContactPhone = "+201888888888" }), default);
        Assert.True(resave.IsSuccess);
        Assert.False(await db.ProfileMedia.AnyAsync(x => x.Id == inheritedMediaId));
        Assert.Equal(edit.Value.OpenRevisionId, resave.Value.OpenRevisionId);
        Assert.False(await db.ProfileMedia.AnyAsync(x =>
            x.ProfileRevisionId == edit.Value.OpenRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery));
        Assert.True(await db.ProfileMedia.AnyAsync(x =>
            x.ProfileRevisionId == facility.ApprovedRevisionId && x.Kind == CareHomeProfileMediaKind.Gallery));
        int versionBeforeRead = facility.Version;
        int documentCount = facility.Documents.Count;
        var read = await new GetMyCareHomeFacilityQueryHandler(db).Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.True(read.IsSuccess);
        Assert.Equal(versionBeforeRead, read.Value.Version);
        Assert.Equal(documentCount, read.Value.Documents.Count);
    }

    private static readonly DateTime FixedNow = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly FixedToday = new(2026, 10, 20);

    [Fact]
    public async Task ApprovedFacility_KeepsNewerUsableDocumentLatest_WhenOlderVerifiedDocumentIsExpired()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = await SaveDraftWithDocumentsAndMediaAsync(
            db, owner, FixedNow, new DateOnly(2026, 1, 1));
        facility.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/OperatingLicense-newer.pdf", "application/pdf", 100, null, FixedNow.AddMinutes(5));
        await db.SaveChangesAsync();

        Guid approvedRevisionId = await SubmitVerifyAndApproveAsync(
            db, facility, owner, admin, FixedNow, FixedNow.AddMinutes(2), FixedToday);

        var edit = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version,
                CareHomeFacilityTests.Draft() with { ContactPhone = "+201999999999" }), default);
        Assert.True(edit.IsSuccess);
        Assert.NotNull(edit.Value.OpenRevisionId);
        Guid openRevisionId = edit.Value.OpenRevisionId!.Value;
        Assert.NotEqual(approvedRevisionId, openRevisionId);

        db.ChangeTracker.Clear();
        CareHomeFacility reloaded = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        List<CareHomeDocument> inheritedLicenses = reloaded.Documents
            .Where(x => x.ProfileRevisionId == openRevisionId && x.Type == CareHomeDocumentType.OperatingLicense)
            .OrderBy(x => x.CreatedOnUtc).ThenBy(x => x.Id).ToList();
        Assert.Equal(2, inheritedLicenses.Count);
        CareHomeDocument olderClone = inheritedLicenses[0];
        CareHomeDocument newerClone = inheritedLicenses[1];

        // Carry-forward preserves the source timestamps, so the newer document stays the
        // effective one for its type and the expired row stays unusable.
        Assert.Equal("private/OperatingLicense.pdf", olderClone.PrivateStorageKey);
        Assert.Equal(FixedNow, olderClone.CreatedOnUtc);
        Assert.Equal(new DateOnly(2026, 1, 1), olderClone.ExpiryDate);
        Assert.Equal(CareHomeDocumentStatus.Verified, olderClone.Status);
        Assert.False(olderClone.IsUsableOn(FixedToday));
        Assert.Equal("private/OperatingLicense-newer.pdf", newerClone.PrivateStorageKey);
        Assert.Equal(FixedNow.AddMinutes(5), newerClone.CreatedOnUtc);
        Assert.Equal(CareHomeDocumentStatus.Verified, newerClone.Status);
        Assert.True(newerClone.IsUsableOn(FixedToday));

        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db).Handle(
            new SubmitMyCareHomeApplicationCommand(owner, reloaded.Version), default);
        Assert.True(submitted.IsSuccess);
        Assert.Equal(openRevisionId, submitted.Value.SubmittedRevisionId);

        // Approval resolves the latest document per type, so it only succeeds while the
        // usable newer clone is still the latest one.
        db.ChangeTracker.Clear();
        CareHomeFacility resubmitted = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        resubmitted.Review(admin, resubmitted.Version, CareHomeReviewAction.Approved, null,
            FixedNow.AddMinutes(30), FixedToday);
        await db.SaveChangesAsync();

        Assert.Equal(openRevisionId, resubmitted.ApprovedRevisionId);
        Assert.Equal(CareHomeStatus.Approved, resubmitted.Status);
    }

    [Fact]
    public async Task ApprovedFacility_ExpiredInheritedDocument_StaysUnusable_AndBlocksApproval()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        DateTime uploadedAt = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        DateTime reviewedAt = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        CareHomeFacility facility = await SaveDraftWithDocumentsAndMediaAsync(
            db, owner, uploadedAt, new DateOnly(2026, 10, 1));
        await SubmitVerifyAndApproveAsync(db, facility, owner, admin, uploadedAt, reviewedAt,
            new DateOnly(2026, 9, 1));

        var edit = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version,
                CareHomeFacilityTests.Draft() with { ContactPhone = "+201888888888" }), default);
        Assert.True(edit.IsSuccess);
        Guid openRevisionId = edit.Value.OpenRevisionId!.Value;

        db.ChangeTracker.Clear();
        CareHomeFacility reloaded = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        CareHomeDocument inheritedLicense = reloaded.Documents.Single(x =>
            x.ProfileRevisionId == openRevisionId && x.Type == CareHomeDocumentType.OperatingLicense);

        // Carry-forward preserves the verified state and the past expiry: it neither
        // restores usability nor flips the row to a non-expiring document.
        Assert.Equal(CareHomeDocumentStatus.Verified, inheritedLicense.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), inheritedLicense.ExpiryDate);
        Assert.False(inheritedLicense.VerifiedNonExpiring);
        Assert.False(inheritedLicense.IsUsableOn(FixedToday));
        Assert.Equal(admin.Value, inheritedLicense.VerifiedByUserId!.Value.Value);
        Guid expiredLicenseId = inheritedLicense.Id;

        // Submission itself only requires the four document types per revision, so the
        // expired document is refused later, by the existing validity rule at review.
        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db).Handle(
            new SubmitMyCareHomeApplicationCommand(owner, reloaded.Version), default);
        Assert.True(submitted.IsSuccess);

        db.ChangeTracker.Clear();
        CareHomeFacility pending = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);

        // The inherited row is already Verified, so an Admin cannot exchange it for a new
        // expiry in place: only a pending document can be verified.
        var reverified = Assert.Throws<DomainException>(() => pending.VerifyDocument(
            admin, pending.Version, expiredLicenseId, new DateOnly(2027, 10, 1), false, reviewedAt));
        Assert.Contains("Only pending care-home documents can be verified", reverified.Message);

        var approval = Assert.Throws<DomainException>(() => pending.Review(
            admin, pending.Version, CareHomeReviewAction.Approved, null, reviewedAt, FixedToday));
        Assert.Contains("must be Admin-verified and valid before approval", approval.Message);
    }

    [Fact]
    public async Task ApprovedFacility_RejectedReplacement_StaysLatestAndBlocksApproval()
    {
        using CareHomesDbContext db = CreateDbContext();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = await SaveDraftWithDocumentsAndMediaAsync(
            db, owner, FixedNow, new DateOnly(2030, 1, 1));
        Guid approvedRevisionId = await SubmitVerifyAndApproveAsync(
            db, facility, owner, admin, FixedNow, FixedNow.AddMinutes(2), FixedToday);

        var edit = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version,
                CareHomeFacilityTests.Draft() with { ContactPhone = "+201777777777" }), default);
        Assert.True(edit.IsSuccess);
        Guid openRevisionId = edit.Value.OpenRevisionId!.Value;

        db.ChangeTracker.Clear();
        CareHomeFacility editing = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        CareHomeDocument replacement = editing.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/OperatingLicense-replacement.pdf", "application/pdf", 100, new DateOnly(2030, 1, 1),
            FixedNow.AddMinutes(30));
        await db.SaveChangesAsync();

        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db).Handle(
            new SubmitMyCareHomeApplicationCommand(owner, editing.Version), default);
        Assert.True(submitted.IsSuccess);

        db.ChangeTracker.Clear();
        CareHomeFacility pending = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        pending.RejectDocument(admin, pending.Version, replacement.Id, "Illegible scan", FixedNow.AddMinutes(35));
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        CareHomeFacility reviewed = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        CareHomeDocument rejectedRow = reviewed.Documents.Single(x => x.Id == replacement.Id);
        CareHomeDocument inheritedLicense = reviewed.Documents.Single(x =>
            x.ProfileRevisionId == openRevisionId && x.Type == CareHomeDocumentType.OperatingLicense &&
            x.Id != replacement.Id);

        // Carry-forward neither validates the rejected replacement nor lets the older
        // verified row become the effective document for the type.
        Assert.Equal(CareHomeDocumentStatus.Rejected, rejectedRow.Status);
        Assert.False(rejectedRow.IsUsableOn(FixedToday));
        Assert.Equal(CareHomeDocumentStatus.Verified, inheritedLicense.Status);
        Assert.True(inheritedLicense.IsUsableOn(FixedToday));
        Assert.True(inheritedLicense.CreatedOnUtc < rejectedRow.CreatedOnUtc);

        var approval = Assert.Throws<DomainException>(() => reviewed.Review(
            admin, reviewed.Version, CareHomeReviewAction.Approved, null, FixedNow.AddMinutes(40), FixedToday));
        Assert.Contains("must be Admin-verified and valid before approval", approval.Message);

        // The rejected row survives the correction round, and the next editable revision
        // inherits the approved revision only: a rejected document never becomes valid.
        reviewed.Review(admin, reviewed.Version, CareHomeReviewAction.CorrectionsRequested,
            "Replace the operating license.", FixedNow.AddMinutes(45), FixedToday);
        await db.SaveChangesAsync();

        db.ChangeTracker.Clear();
        CareHomeFacility corrected = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        var next = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, corrected.Version,
                CareHomeFacilityTests.Draft() with { ContactPhone = "+201666666666" }), default);
        Assert.True(next.IsSuccess);
        Guid nextRevisionId = next.Value.OpenRevisionId!.Value;
        Assert.NotEqual(openRevisionId, nextRevisionId);

        db.ChangeTracker.Clear();
        CareHomeFacility withNext = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        Assert.Equal(CareHomeDocumentStatus.Rejected,
            withNext.Documents.Single(x => x.Id == replacement.Id).Status);
        List<CareHomeDocument> nextLicenses = withNext.Documents
            .Where(x => x.ProfileRevisionId == nextRevisionId && x.Type == CareHomeDocumentType.OperatingLicense)
            .ToList();
        CareHomeDocument nextLicense = Assert.Single(nextLicenses);
        Assert.Equal("private/OperatingLicense.pdf", nextLicense.PrivateStorageKey);
        Assert.Equal(CareHomeDocumentStatus.Verified, nextLicense.Status);
        Assert.True(nextLicense.IsUsableOn(FixedToday));
        Assert.Equal(approvedRevisionId, withNext.ApprovedRevisionId);
    }

    private static async Task<CareHomeFacility> SaveDraftWithDocumentsAndMediaAsync(
        CareHomesDbContext db, UserId owner, DateTime uploadedAt, DateOnly? licenseExpiry)
    {
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        Assert.True(saved.IsSuccess);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
        {
            DateOnly? expiry = type == CareHomeDocumentType.OperatingLicense
                ? licenseExpiry
                : new DateOnly(2030, 1, 1);
            facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, expiry, uploadedAt);
        }

        db.ProfileMedia.Add(CoverMedia(facility));
        db.ProfileMedia.Add(GalleryMedia(facility, 0));
        await db.SaveChangesAsync();
        return facility;
    }

    private static async Task<Guid> SubmitVerifyAndApproveAsync(
        CareHomesDbContext db, CareHomeFacility facility, UserId owner, UserId admin,
        DateTime uploadedAt, DateTime reviewedAt, DateOnly reviewedOn)
    {
        var submitted = await new SubmitMyCareHomeApplicationCommandHandler(db).Handle(
            new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
        Assert.True(submitted.IsSuccess);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, document.ExpiryDate,
                document.ExpiryDate is null, uploadedAt.AddMinutes(1));
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, reviewedAt, reviewedOn);
        await db.SaveChangesAsync();
        Assert.Equal(CareHomeStatus.Approved, facility.Status);
        return facility.ApprovedRevisionId!.Value;
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public List<string> DeletedKeys { get; } = [];
        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType,
            long contentLength, string folder, CancellationToken cancellationToken = default) =>
            SavePrivateAsync(content, contentType, contentLength, folder, cancellationToken);
        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType,
            long contentLength, string folder, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result<StoredFile>.Success(new StoredFile($"private/{folder}/{Guid.NewGuid():N}.png")));
        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(Result.Success());
        }
    }

    private static CareHomesDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
