using System.Reflection;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Facilities;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// Focused authored checks for the 2026-10-27 document lifecycle work:
/// additive current/pending/history classification with the overlapping
/// IsExpired axis (Cairo clock), the dedicated owner/admin document lists,
/// the scoped owner file read, and the history-only logical removal.
/// Authored under the phase source freeze; executed with the phase gate
/// after Tasks A and B freeze.
/// </summary>
public sealed class CareHomeDocumentLifecycleTests
{
    // Cairo date of this instant is 2030-01-02 (UTC+2 winter offset).
    private static readonly DateTime CairoMorning = new(2030, 1, 2, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CurrentApproved_StaysVisibleUnderPendingReplacement_AndExpiredStaysCurrentWithoutFallback()
    {
        using CareHomesDbContext db = CreateDb();
        (CareHomeFacility facility, UserId owner) = await SeedApprovedAsync(db, healthExpiry: new DateOnly(2030, 1, 2));

        // Owner edits the approved facility: documented clones appear in
        // revision 2 (Verified, same CreatedOnUtc) and a brand-new license
        // replacement is uploaded there.
        var edit = await new SaveMyCareHomeProfileCommandHandler(db, new CareHomeTestClock(CairoMorning)).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version, CareHomeFacilityTests.Draft()), default);
        Assert.True(edit.IsSuccess);
        CareHomeProfileRevision open = facility.Revisions.Single(x => !x.IsFrozen);
        facility.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/license-replacement.pdf", "application/pdf", 100, null, CairoMorning);
        await db.SaveChangesAsync();

        var read = await new GetMyCareHomeFacilityQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.True(read.IsSuccess);

        // The clone tie is real: the health clone in the open revision shares
        // the approved document's CreatedOnUtc (the shared comparator falls
        // back to Id — the same rule the admin ordering uses).
        CareHomeDocument openHealthClone = SingleDocument(facility, open.Id, CareHomeDocumentType.HealthCertificate);
        CareHomeDocument approvedLicense = SingleDocument(facility, ApprovedRevision(facility), CareHomeDocumentType.OperatingLicense);

        CareHomeDocumentResponse license = Document(read.Value, approvedLicense.Id);
        Assert.Equal(CareHomeDocumentViews.Current, license.LifecycleView);
        Assert.False(license.IsExpired);

        // Newest per-type document of the open revision is the pending
        // candidate — even though it is newer than the current license, it
        // never displaces it.
        // The uploaded replacement is the NEWEST license of the open
        // revision (SaveDraft clones every approved document into the fresh
        // revision, so the open revision holds both the license clone —
        // superseded, history — and this upload: pending candidate).
        CareHomeDocument replacement = facility.Documents
            .Where(x => x.ProfileRevisionId == open.Id && x.Type == CareHomeDocumentType.OperatingLicense)
            .OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id)
            .First();
        CareHomeDocumentResponse pendingLicense = Document(read.Value, replacement.Id);
        Assert.Equal(CareHomeDocumentViews.Pending, pendingLicense.LifecycleView);
        Assert.False(pendingLicense.NeedsAction);
        Assert.NotEqual(license.Id, pendingLicense.Id);
        // The health clone is the newest health document of the unapproved
        // open revision — it is a pending candidate (candidate-of-revision),
        // never a second current, even though it is Verified.
        CareHomeDocumentResponse openHealth = Document(read.Value, openHealthClone.Id);
        Assert.Equal(CareHomeDocumentViews.Pending, openHealth.LifecycleView);

        // Backcompat: the owner Documents array keeps the aggregate insertion
        // order (minus logically removed rows), each item gaining only the
        // additive lifecycle fields — no duplicates, no reordering.
        IReadOnlyList<CareHomeDocumentResponse> ownerArray = read.Value!.Documents;
        CareHomeDocument[] aggregateOrder = facility.Documents.Where(x => !x.IsRemoved).ToArray();
        Assert.Equal(aggregateOrder.Length, ownerArray.Count);
        for (int i = 0; i < aggregateOrder.Length; i++)
            Assert.Equal(aggregateOrder[i].Id, ownerArray[i].Id);
    }

    [Fact]
    public async Task ExpiredAxis_UsesSuppliedDate_InAndOutOfCairoBoundary_WithoutFalseReassurance()
    {
        using CareHomesDbContext db = CreateDb();
        (CareHomeFacility facility, UserId owner) = await SeedApprovedAsync(db, healthExpiry: new DateOnly(2030, 1, 2));
        Guid approvedRevision = ApprovedRevision(facility);
        CareHomeDocument health = facility.Documents.Single(x =>
            x.ProfileRevisionId == approvedRevision && x.Type == CareHomeDocumentType.HealthCertificate);

        // Cairo boundary: expiry == Cairo-today is still usable (not
        // expired).
        var today = await ReadOwnerProfile(db, owner);
        CareHomeDocumentResponse healthToday = Document(today, health.Id);
        Assert.Equal(CareHomeDocumentViews.Current, healthToday.LifecycleView);
        Assert.False(healthToday.IsExpired);

        // The next Cairo day the same document is expired yet stays current
        // with the flag set; no older verified document is promoted.
        var nextDay = await ReadOwnerProfile(db, owner, new CareHomeTestClock(new DateTime(2030, 1, 3, 9, 0, 0, DateTimeKind.Utc)));
        CareHomeDocumentResponse healthNext = Document(nextDay, health.Id);
        Assert.Equal(CareHomeDocumentViews.Current, healthNext.LifecycleView);
        Assert.True(healthNext.IsExpired);
    }

    [Fact]
    public async Task FreshPendingDocument_WithPastSuppliedExpiry_IsExpiredFlag_True_NotCurrent()
    {
        using CareHomesDbContext db = CreateDb();
        (CareHomeFacility facility, UserId owner) = await SeedApprovedAsync(db, healthExpiry: null);
        var edit = await new SaveMyCareHomeProfileCommandHandler(db, new CareHomeTestClock(CairoMorning)).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version, CareHomeFacilityTests.Draft()), default);
        Assert.True(edit.IsSuccess);
        var upload = await new UploadCareHomeDocumentCommandHandler(db, new RecordingStorage(), new CareHomeTestClock(CairoMorning))
            .Handle(new UploadCareHomeDocumentCommand(
                owner, facility.Version, CareHomeDocumentType.OperatingLicense, new DateOnly(2020, 1, 1),
                "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7\nfixture").Length,
                new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nfixture"))), default);

        Assert.True(upload.IsSuccess);
        Assert.Equal(CareHomeDocumentViews.Pending, upload.Value.LifecycleView);
        // Owner-override semantics: the supplied past date is reported as
        // expired even though the document is a pending candidate — Status
        // (PendingReview) is the separate field that stops it from reading
        // as approved.
        Assert.True(upload.Value.IsExpired);
        Assert.Equal(CareHomeDocumentStatus.PendingReview, upload.Value.Status);
        Assert.False(upload.Value.NeedsAction);
        // The candidate never becomes current just because it is newer.
        Assert.NotEqual(CareHomeDocumentViews.Current, upload.Value.LifecycleView);
        Assert.Equal(CareHomeDocumentViews.Pending, upload.Value.LifecycleView);
    }

    [Fact]
    public async Task RejectedCandidate_StaysPendingView_WithNeedsAction()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        CareHomeDocument license = facility.Documents.Single(x => x.Type == CareHomeDocumentType.OperatingLicense);
        facility.RejectDocument(reviewer, facility.Version, license.Id, "unreadable scan", Now.AddMinutes(1));
        await db.SaveChangesAsync();

        var read = await ReadOwnerProfile(db, owner);
        Guid submitted = facility.SubmittedRevisionId!.Value;
        CareHomeDocumentResponse rejected = Document(read, license.Id);
        Assert.Equal(CareHomeDocumentViews.Pending, rejected.LifecycleView);
        Assert.Equal(CareHomeDocumentStatus.Rejected, rejected.Status);
        Assert.True(rejected.NeedsAction);
        Assert.Equal(submitted, read.ReviewTargetRevisionId);
    }

    [Fact]
    public async Task MultipleVerifiedInApprovedRevision_NewestIsCurrent_OldersHistory()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/license-second.pdf", "application/pdf", 100, null, Now.AddMinutes(1));
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(2));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(3), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        Assert.True(facility.Documents.Count(x => x.Type == CareHomeDocumentType.OperatingLicense
            && x.Status == CareHomeDocumentStatus.Verified) == 2);

        var read = await ReadOwnerProfile(db, owner);
        Guid approved = facility.ApprovedRevisionId!.Value;
        var licenses = read.Documents.Where(x => x.Type == CareHomeDocumentType.OperatingLicense).ToArray();
        Assert.Equal(2, licenses.Length);
        Assert.Equal(1, licenses.Count(x => x.LifecycleView == CareHomeDocumentViews.Current));
        Assert.Equal(1, licenses.Count(x => x.LifecycleView == CareHomeDocumentViews.History));
        CareHomeDocumentResponse current = licenses.Single(x => x.LifecycleView == CareHomeDocumentViews.Current);
        // The newer second upload (distinct CreatedOnUtc) is current.
        CareHomeDocument newer = facility.Documents
            .Where(x => x.ProfileRevisionId == approved && x.Type == CareHomeDocumentType.OperatingLicense)
            .OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id).First();
        Assert.Equal(newer.Id, current.Id);
    }

    [Fact]
    public async Task HistoryRemoval_IsLogical_SecondDeleteIdempotent_ProtectedSlotsRefused()
    {
        using CareHomesDbContext db = CreateDb();
        var storage = new RecordingStorage();
        (CareHomeFacility facility, UserId owner) = await SeedApprovedAsync(db, healthExpiry: null);
        var edit = await new SaveMyCareHomeProfileCommandHandler(db, new CareHomeTestClock(CairoMorning)).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version, CareHomeFacilityTests.Draft()), default);
        Assert.True(edit.IsSuccess);

        // Revision-2 clone of the license is superseded (revision-2's
        // per-type latest is the fresh replacement we upload) — history,
        // therefore removable.
        var replacement = await new UploadCareHomeDocumentCommandHandler(db, storage, new CareHomeTestClock(CairoMorning))
            .Handle(new UploadCareHomeDocumentCommand(
                owner, facility.Version, CareHomeDocumentType.OperatingLicense, null,
                "application/pdf", Encoding.ASCII.GetBytes("%PDF-1.7\nreplacement").Length,
                new MemoryStream(Encoding.ASCII.GetBytes("%PDF-1.7\nreplacement"))), default);
        Assert.True(replacement.IsSuccess);
        Guid approvedRevision = ApprovedRevision(facility);
        CareHomeDocument supersededClone = facility.Documents.Single(x =>
            x.ProfileRevisionId != approvedRevision &&
            x.Type == CareHomeDocumentType.OperatingLicense &&
            x.Id != replacement.Value.Id);
        Guid approvedLicenseId = facility.Documents.Single(x =>
            x.ProfileRevisionId == approvedRevision && x.Type == CareHomeDocumentType.OperatingLicense).Id;

        int versionBefore = facility.Version;
        var removed = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, versionBefore, supersededClone.Id), default);
        Assert.True(removed.IsSuccess);
        Assert.True(supersededClone.IsRemoved);
        int deletesAfterFirst = storage.DeleteCalls;
        Assert.Equal(0, deletesAfterFirst); // logical only: bytes never deleted

        // Idempotent replay: the same DELETE reports success again even with
        // a deliberately stale version, and writes nothing.
        var replay = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, versionBefore, supersededClone.Id), default);
        Assert.True(replay.IsSuccess);
        Assert.Equal(0, storage.DeleteCalls);

        // Reads no longer show the removed row.
        db.ChangeTracker.Clear();
        var read = await new GetMyCareHomeFacilityQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.DoesNotContain(read.Value!.Documents, x => x.Id == supersededClone.Id);
        var list = await new GetMyCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeDocumentsQuery(owner, CareHomeDocumentViews.History, null, null), default);
        Assert.DoesNotContain(list.Value!.Items, x => x.Id == supersededClone.Id);
        var file = await new GetMyCareHomeDocumentFileQueryHandler(db, storage)
            .Handle(new GetMyCareHomeDocumentFileQuery(owner, supersededClone.Id), default);
        Assert.False(file.IsSuccess);
        Assert.Equal("CareHomes.Document.DocumentNotFound", file.Error.Code);

        // The approved current document still works through its own id even
        // though a removed clone historically shared its storage key.
        CareHomeFacility fresh = await LoadFacility(db, owner);
        CareHomeDocument currentLicense = fresh.Documents.Single(x =>
            x.ProfileRevisionId == approvedRevision && x.Type == CareHomeDocumentType.OperatingLicense && !x.IsRemoved);
        var currentFile = await new GetMyCareHomeDocumentFileQueryHandler(db, storage)
            .Handle(new GetMyCareHomeDocumentFileQuery(owner, currentLicense.Id), default);
        Assert.True(currentFile.IsSuccess);
        // The current document is still served through its own live id while
        // the removed history clone's ids are dead ends.
        Assert.Equal(currentLicense.PrivateStorageKey, currentFile.Value.Key);

        // Protected slots: the effective current document — even when flagged
        // expired on the IsExpired axis — and the active pending replacement
        // can never be removed.
        var currentRemoval = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, fresh.Version, currentLicense.Id), default);
        Assert.False(currentRemoval.IsSuccess);
        Assert.Equal("CareHomes.Document.NotRemovable", currentRemoval.Error.Code);
        var pendingRemoval = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, fresh.Version, replacement.Value.Id), default);
        Assert.False(pendingRemoval.IsSuccess);
        Assert.Equal("CareHomes.Document.NotRemovable", pendingRemoval.Error.Code);

        // Version concurrency and cross-owner scoping.
        var staleVersion = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, fresh.Version - 5, approvedLicenseId), default);
        Assert.False(staleVersion.IsSuccess);
        Assert.Equal("CareHomes.Document.Conflict", staleVersion.Error.Code);
        UserId stranger = UserId.New();
        var foreign = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(stranger, 1, currentLicense.Id), default);
        Assert.False(foreign.IsSuccess);
        Assert.Equal("CareHomes.Document.FacilityNotFound", foreign.Error.Code);
    }

    [Fact]
    public async Task Removal_Filters_ReachDomainAndReads()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        // Approved revision with TWO verified licenses: newest is current,
        // the older one is history and removable.
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/license-second.pdf", "application/pdf", 100, null, Now.AddMinutes(1));
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(2));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(3), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        Guid approved = facility.ApprovedRevisionId!.Value;
        CareHomeDocument oldestLicense = facility.Documents
            .Where(x => x.Type == CareHomeDocumentType.OperatingLicense && x.ProfileRevisionId == approved)
            .OrderBy(x => x.CreatedOnUtc).ThenBy(x => x.Id).First();
        CareHomeFacility tracked = await LoadFacility(db, owner);
        var removed = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, tracked.Version, oldestLicense.Id), default);
        Assert.True(removed.IsSuccess);

        // Clone-on-edit must not carry removed rows into the new revision.
        facility = await LoadFacility(db, owner);
        var edit = await new SaveMyCareHomeProfileCommandHandler(db, new CareHomeTestClock(CairoMorning)).Handle(
            new SaveMyCareHomeProfileCommand(owner, facility.Version, CareHomeFacilityTests.Draft()), default);
        Assert.True(edit.IsSuccess);
        db.ChangeTracker.Clear();
        facility = await LoadFacility(db, owner);
        Guid openRevision = facility.Revisions.Single(x => !x.IsFrozen).Id;
        Assert.DoesNotContain(facility.Documents, x => x.ProfileRevisionId == openRevision && x.Id == oldestLicense.Id);
        Assert.Equal(approved, facility.ApprovedRevisionId);
    }

    [Fact]
    public async Task NegativeNewestApprovedSlot_NoCurrentSurvivesHistoryRemoval_AndBlocksReactivation()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        // Approved revision with TWO verified licenses (same shape as the
        // multiple-verified scenario). The legacy/data-safety state under
        // test is then forced onto the NEWEST license: a Rejected newest
        // approved-slot document. No API authoring path can produce this
        // (the review gates refuse a negative latest and Revoked is a
        // reserved value), but the classifier explicitly supports the state
        // and the no-resurrection requirement must hold on raw stored rows.
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/license-second.pdf", "application/pdf", 100, null, Now.AddMinutes(1));
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(2));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(3), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        facility = await LoadFacility(db, owner);
        Guid approved = facility.ApprovedRevisionId!.Value;
        CareHomeDocument[] orderedLicenses = facility.Documents
            .Where(x => x.ProfileRevisionId == approved && x.Type == CareHomeDocumentType.OperatingLicense)
            .OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id).ToArray();
        Assert.Equal(2, orderedLicenses.Length);
        CareHomeDocument newest = orderedLicenses[0];
        CareHomeDocument older = orderedLicenses[1];
        ForceLegacyStatus(newest, CareHomeDocumentStatus.Rejected);
        await db.SaveChangesAsync();

        // Before: the negative newest blocks the whole slot — no current for
        // the license type at all, and the older verified document is history.
        var before = await ReadOwnerProfile(db, owner);
        Assert.DoesNotContain(before.Documents, x => x.Type == CareHomeDocumentType.OperatingLicense
            && x.LifecycleView == CareHomeDocumentViews.Current);
        Assert.Equal(CareHomeDocumentViews.History, Document(before, older.Id).LifecycleView);
        Assert.Equal(CareHomeDocumentViews.History, Document(before, newest.Id).LifecycleView);

        // The negative newest is history-classified and therefore removable.
        var removed = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, facility.Version, newest.Id), default);
        Assert.True(removed.IsSuccess);

        // After: removing the negative row must NOT resurrect the older
        // verified document — the no-current verdict is retained; eligibility
        // restrictions stay in force through the read layer.
        var after = await ReadOwnerProfile(db, owner);
        Assert.DoesNotContain(after.Documents, x => x.Id == newest.Id);
        Assert.DoesNotContain(after.Documents, x => x.Type == CareHomeDocumentType.OperatingLicense
            && x.LifecycleView == CareHomeDocumentViews.Current);
        Assert.Equal(CareHomeDocumentViews.History, Document(after, older.Id).LifecycleView);

        // The domain gates agree with the classifier: the latest-per-type
        // decision includes the removed negative row, so reactivation cannot
        // be unblocked by the removal.
        facility = await LoadFacility(db, owner);
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Suspended, "ops", Now.AddMinutes(4), DateOnly.FromDateTime(Now));
        Assert.Equal(CareHomeStatus.Suspended, facility.Status);
        Assert.Throws<DomainException>(() => facility.Review(
            reviewer, facility.Version, CareHomeReviewAction.Reactivated, null, Now.AddMinutes(5), DateOnly.FromDateTime(Now)));
        Assert.Equal(CareHomeStatus.Suspended, facility.Status);
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task ExpiredCurrentDocument_IsRemovableRefused()
    {
        using CareHomesDbContext db = CreateDb();
        // The health certificate is usable at review time (expiry after the
        // review date) but long past the Cairo date of the removal clock —
        // it becomes an expired current document.
        (CareHomeFacility facility, UserId owner) = await SeedApprovedAsync(db, healthExpiry: new DateOnly(2026, 10, 28));

        var read = await ReadOwnerProfile(db, owner);
        CareHomeDocumentResponse currentHealth = read.Documents.Single(x =>
            x.Type == CareHomeDocumentType.HealthCertificate);
        Assert.Equal(CareHomeDocumentViews.Current, currentHealth.LifecycleView);
        Assert.True(currentHealth.IsExpired);

        var refused = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, facility.Version, currentHealth.Id), default);
        Assert.False(refused.IsSuccess);
        Assert.Equal("CareHomes.Document.NotRemovable", refused.Error.Code);

        // Protection is not cosmetic: the row stays unremoved and current.
        facility = await LoadFacility(db, owner);
        Assert.False(facility.Documents.Single(x => x.Id == currentHealth.Id).IsRemoved);
        var reread = await ReadOwnerProfile(db, owner);
        Assert.Equal(CareHomeDocumentViews.Current, Document(reread, currentHealth.Id).LifecycleView);
    }

    [Fact]
    public async Task DocumentLists_DefaultAllLabeled_FiltersValidated_CodedErrors()
    {
        using CareHomesDbContext db = CreateDb();
        (CareHomeFacility facility, UserId owner) = await SeedApprovedAsync(db, healthExpiry: null);
        var all = await new GetMyCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeDocumentsQuery(owner, null, null, null), default);
        Assert.True(all.IsSuccess);
        Assert.Equal(CareHomeDocumentViews.All, all.Value!.View);
        Assert.Equal(all.Value!.Items.Count, all.Value!.TotalCount);
        Assert.All(all.Value!.Items, item => Assert.Contains(item.LifecycleView,
            new[] { CareHomeDocumentViews.Current, CareHomeDocumentViews.Pending, CareHomeDocumentViews.History }));
        Assert.Equal(all.Value!.Items.Count(x => x.LifecycleView == CareHomeDocumentViews.Current),
            all.Value!.Items.GroupBy(x => x.Type).Count(x => x.Any(i => i.LifecycleView == CareHomeDocumentViews.Current)));

        var currentOnly = await new GetMyCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeDocumentsQuery(owner, CareHomeDocumentViews.Current, CareHomeDocumentType.HealthCertificate, null), default);
        Assert.True(currentOnly.IsSuccess);
        Assert.Single(currentOnly.Value!.Items);
        Assert.Equal(CareHomeDocumentType.HealthCertificate, currentOnly.Value!.Items[0].Type);

        var badView = await new GetMyCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeDocumentsQuery(owner, "bogus", null, null), default);
        Assert.False(badView.IsSuccess);
        Assert.Equal("CareHomes.Document.InvalidView", badView.Error.Code);

        var badType = await new GetMyCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeDocumentsQuery(owner, null, (CareHomeDocumentType)99, null), default);
        Assert.False(badType.IsSuccess);
        Assert.Equal("CareHomes.Document.InvalidType", badType.Error.Code);

        // Admin list shares the projection and the validation.
        var adminAll = await new GetAdminCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetAdminCareHomeDocumentsQuery(facility.Id.Value, null, null, null), default);
        Assert.True(adminAll.IsSuccess);
        Assert.Equal(all.Value!.TotalCount, adminAll.Value!.TotalCount);
        var adminBadView = await new GetAdminCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetAdminCareHomeDocumentsQuery(facility.Id.Value, "nope", null, null), default);
        Assert.Equal("CareHomes.Document.InvalidView", adminBadView.Error.Code);

        // Unknown facility → coded 404; nothing about other facilities leaks.
        var missing = await new GetAdminCareHomeDocumentsQueryHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new GetAdminCareHomeDocumentsQuery(Guid.NewGuid(), null, null, null), default);
        Assert.Equal("CareHomes.Admin.ApplicationNotFound", missing.Error.Code);
    }

    [Fact]
    public async Task ReviewGate_IgnoresRemovedHistoryRows_NoEligibilityChange()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        // Approved with TWO verified licenses: newest is current, the older
        // one is history — removable without touching any eligibility gate.
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/license-second.pdf", "application/pdf", 100, null, Now.AddMinutes(1));
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(2));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(3), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        facility = await LoadFacility(db, owner);
        Guid approved = facility.ApprovedRevisionId!.Value;
        CareHomeDocument oldestLicense = facility.Documents
            .Where(x => x.Type == CareHomeDocumentType.OperatingLicense && x.ProfileRevisionId == approved)
            .OrderBy(x => x.CreatedOnUtc).ThenBy(x => x.Id).First();
        var removed = await new RemoveMyCareHomeDocumentCommandHandler(db, new CareHomeTestClock(CairoMorning))
            .Handle(new RemoveMyCareHomeDocumentCommand(owner, facility.Version, oldestLicense.Id), default);
        Assert.True(removed.IsSuccess);

        // Direct review of a removed row fails closed — no reanimation.
        // (The tracked aggregate lookup ignores removed rows entirely.)
        Assert.Throws<DomainException>(() => facility.VerifyDocument(
            reviewer, facility.Version, oldestLicense.Id, null, true, Now.AddMinutes(4)));

        // Suspend → reactivate: the reactivation gate re-checks the approved
        // revision ignoring the removed row (the newest verified license
        // still covers the type), so the outcome is exactly as before the
        // lifecycle work.
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Suspended, "ops", Now.AddMinutes(4), DateOnly.FromDateTime(Now));
        Assert.Equal(CareHomeStatus.Suspended, facility.Status);
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Reactivated, null, Now.AddMinutes(5), DateOnly.FromDateTime(Now));
        Assert.Equal(CareHomeStatus.Approved, facility.Status);
        await db.SaveChangesAsync();

        // Reads: exactly one current license remains; the removed row is gone.
        var read = await ReadOwnerProfile(db, owner);
        var licenses = read.Documents.Where(x => x.Type == CareHomeDocumentType.OperatingLicense).ToArray();
        Assert.Single(licenses);
        Assert.Equal(CareHomeDocumentViews.Current, licenses[0].LifecycleView);
        Assert.DoesNotContain(licenses, x => x.Id == oldestLicense.Id);
    }

    private static async Task<(CareHomeFacility, UserId)> SeedApprovedAsync(
        CareHomesDbContext db, DateOnly? healthExpiry)
    {
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
        {
            if (document.Type == CareHomeDocumentType.HealthCertificate)
            {
                if (healthExpiry is DateOnly expiry)
                    facility.VerifyDocument(reviewer, facility.Version, document.Id, expiry, false, Now.AddMinutes(1));
                else
                    facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(1));
            }
            else
            {
                facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(1));
            }
        }
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(2), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        return (facility, owner);
    }

    private static Guid ApprovedRevision(CareHomeFacility facility) => facility.ApprovedRevisionId!.Value;

    /// <summary>
    /// Simulates the legacy/data-safety state the classifier and the reserved
    /// Revoked value explicitly support (the newest approved-revision document
    /// of a type is Rejected/Revoked). No API authoring path can produce it —
    /// the review gates refuse a negative latest — so the state machine is
    /// bypassed deliberately to prove the no-resurrection semantics on raw
    /// stored states.
    /// </summary>
    private static void ForceLegacyStatus(CareHomeDocument document, CareHomeDocumentStatus status)
    {
        var property = typeof(CareHomeDocument).GetProperty(nameof(CareHomeDocument.Status))
            ?? throw new InvalidOperationException("CareHomeDocument.Status property not found.");
        property.SetValue(document, status);
    }

    private static CareHomeDocument SingleDocument(CareHomeFacility facility, Guid revisionId, CareHomeDocumentType type) =>
        facility.Documents.Single(x => x.ProfileRevisionId == revisionId && x.Type == type);

    private static CareHomeDocumentResponse Document(CareHomeOwnerProfile profile, Guid id) =>
        profile.Documents.Single(x => x.Id == id);

    private static async Task<CareHomeOwnerProfile> ReadOwnerProfile(
        CareHomesDbContext db, UserId owner, IDateTimeProvider? clock = null)
    {
        var read = await new GetMyCareHomeFacilityQueryHandler(db, clock ?? new CareHomeTestClock(CairoMorning))
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.True(read.IsSuccess);
        return read.Value!;
    }

    private static async Task<CareHomeFacility> LoadFacility(CareHomesDbContext db, UserId owner)
    {
        db.ChangeTracker.Clear();
        return await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .AsSplitQuery().SingleAsync(x => x.OwnerUserId == owner);
    }

    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0, DateTimeKind.Utc);

    private static CareHomeFacility NewDraftFacility(UserId owner)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now);
        return facility;
    }

    private static void AddRequiredDocuments(CareHomeFacility facility, UserId owner)
    {
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, Now);
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecordingStorage : IFileStorage
    {
        public int SaveCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public int OpenCalls { get; private set; }

        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default)
        {
            SaveCalls++;
            return Task.FromResult<Result<StoredFile>>(new StoredFile($"private/{folder}/{SaveCalls}.pdf"));
        }

        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            return Task.FromResult<Result<PrivateFileContent>>(
                new PrivateFileContent(key, "application/pdf", new MemoryStream([1, 2, 3])));
        }

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            return Task.FromResult(Result.Success());
        }
    }
}
