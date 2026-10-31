using Microsoft.EntityFrameworkCore;
using MediatR;
using Npgsql;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Application.Facilities;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Users;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace Ahlena.UnitTests.CareHomes;

/// <summary>
/// FUR-04 relational proof against a validated disposable PostgreSQL target.
/// Executes the exact production read query handlers (owner profile, Admin
/// application, owner operational booking, Admin disputes) plus the exact
/// owner/Admin/public profile-media file-guard predicates through Npgsql on
/// PostgreSQL. Applies only the existing committed CareHomes and Identity
/// migrations to the disposable target. Never resets, drops, truncates, or
/// broadly seeds the database. Only inserts minimal fixture rows and deletes
/// them by ID in a finally block. The target connection string must be
/// supplied as ConnectionStrings__Fur04VerificationDatabase in the explicit
/// test child process only; the guarded suite leaves it absent and these tests
/// skip. Endpoint and database name are validated before any write; anything
/// else fails closed without touching a database.
/// </summary>
[Collection("Fur04Verification")]
public sealed class CareHomeReadbackPostgresTests
{
    private const string ConnectionStringVariable = "ConnectionStrings__Fur04VerificationDatabase";

    private const string ExpectedDatabase = "AhlenaFur04VerifyDb";

    private const string ExpectedCareHomesHead = "20261027120000_AddCareHomeDocumentHistoryRemoval";

    private const string ExpectedIdentityHead = "20261022094500_AddPartnerContactOptionalFields";

    // 2026-10-27 release fact-check: all 20 committed CareHomes migrations
    // are recorded (including the empty-Up 20261003222617 — the old comment
    // claiming it is not recorded was stale), plus 17 Identity rows.
    private const int ExpectedCareHomesMigrationCount = 20;

    private const int ExpectedIdentityMigrationCount = 17;

    private static readonly DateTime PgNow = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly SemaphoreSlim MigrationLock = new(1, 1);

    private static string? _migratedFor;

    private static int _phoneSeq = 500;

    [Fur04PostgresFact]
    public async Task MigrationHeads_MatchCommittedCareHomesAndIdentityBaseline()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        await using var careHomes = CreateCareHomesDb(connectionString);
        // Both module contexts share the single public history table; the two
        // committed sets are disjoint timestamped IDs. Committed migration
        // 20261003222617_AddCareHomeBookingsMetadata has an empty Up() and is
        // not recorded by Migrate(), hence 18 CareHomes rows instead of 19.
        // The CareHomes refund-policy head (2026-10-22 21:30) sorts after the
        // Identity partner-contact head (2026-10-22 09:45), so it is the final
        // history row.
        List<string> heads = await careHomes.Database
            .SqlQueryRaw<string>("SELECT \"MigrationId\" FROM public.\"__EFMigrationsHistory\" ORDER BY \"MigrationId\"")
            .ToListAsync();

        Assert.Equal(ExpectedCareHomesMigrationCount + ExpectedIdentityMigrationCount, heads.Count);
        Assert.Contains(ExpectedCareHomesHead, heads);
        Assert.Contains(ExpectedIdentityHead, heads);
        Assert.Equal(ExpectedCareHomesHead, heads[^1]);
    }

    [Fur04PostgresFact]
    public async Task OwnerProfile_PendingReview_BindsSubmittedRevision()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        UserId owner = UserId.New();
        List<CareHomeId> facilityIds = [];
        try
        {
            await using (var setupDb = CreateCareHomesDb(connectionString))
            {
                CareHomeFacility facility = NewDraftFacility(owner);
                AddRequiredDocuments(facility, owner);
                facility.Submit(owner, facility.Version, PgNow);
                setupDb.Facilities.Add(facility);
                await setupDb.SaveChangesAsync();
                facilityIds.Add(facility.Id);
                Assert.Equal(1, await setupDb.Facilities.CountAsync(x => x.Id == facility.Id));
            }

            await using var readDb = CreateCareHomesDb(connectionString);
            var read = await new GetMyCareHomeFacilityQueryHandler(readDb, CareHomeTestClockHost.Clock())
                .Handle(new GetMyCareHomeFacilityQuery(owner), default);

            Assert.True(read.IsSuccess);
            Assert.Equal(CareHomeStatus.PendingReview, read.Value.Status);
            Assert.NotNull(read.Value.SubmittedProfile);
            Assert.Equal(read.Value.SubmittedRevisionId, read.Value.SubmittedProfile!.RevisionId);
            Assert.Equal("Hope House", read.Value.SubmittedProfile.Profile.EnglishName);
            Assert.Null(read.Value.ApprovedProfile);
        }
        finally
        {
            await CleanupAsync(connectionString, facilityIds, [], [], [], []);
        }
    }

    [Fur04PostgresFact]
    public async Task OwnerProfile_ApprovedWithNewerDraft_KeepsApprovedReadback()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        List<CareHomeId> facilityIds = [];
        Guid approvedRevisionId;
        try
        {
            await using (var setupDb = CreateCareHomesDb(connectionString))
            {
                CareHomeFacility facility = NewDraftFacility(owner);
                AddRequiredDocuments(facility, owner);
                facility.Submit(owner, facility.Version, PgNow);
                foreach (CareHomeDocument document in facility.Documents.ToArray())
                    facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, PgNow.AddMinutes(1));
                facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, PgNow.AddMinutes(2), DateOnly.FromDateTime(PgNow));
                approvedRevisionId = facility.ApprovedRevisionId!.Value;
                setupDb.Facilities.Add(facility);
                await setupDb.SaveChangesAsync();
                facilityIds.Add(facility.Id);

                CareHomeFacility editable = await setupDb.Facilities.Include(x => x.Revisions)
                    .SingleAsync(x => x.Id == facility.Id);
                editable.SaveDraft(owner, editable.Version, CareHomeFacilityTests.Draft() with { Address = "Updated address" }, PgNow.AddMinutes(3));
                await setupDb.SaveChangesAsync();
            }

            await using var readDb = CreateCareHomesDb(connectionString);
            var read = await new GetMyCareHomeFacilityQueryHandler(readDb, CareHomeTestClockHost.Clock())
                .Handle(new GetMyCareHomeFacilityQuery(owner), default);

            Assert.True(read.IsSuccess);
            Assert.Equal(CareHomeStatus.Approved, read.Value.Status);
            Assert.NotNull(read.Value.Draft);
            Assert.Equal("Updated address", read.Value.Draft!.Address);
            Assert.Null(read.Value.SubmittedProfile);
            Assert.NotNull(read.Value.ApprovedProfile);
            Assert.Equal(approvedRevisionId, read.Value.ApprovedProfile!.RevisionId);
        }
        finally
        {
            await CleanupAsync(connectionString, facilityIds, [], [], [], []);
        }
    }

    [Fur04PostgresFact]
    public async Task AdminApplication_MediaOrderedWithAdminUri_AndFileGuardPredicatesTranslate()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        List<CareHomeId> facilityIds = [];
        List<Guid> mediaIds = [];
        Guid coverId;
        Guid galleryId;
        try
        {
            await using (var setupDb = CreateCareHomesDb(connectionString))
            {
                CareHomeFacility facility = NewDraftFacility(owner);
                AddRequiredDocuments(facility, owner);
                Guid revisionId = facility.Revisions.Single().Id;
                facility.Submit(owner, facility.Version, PgNow);
                foreach (CareHomeDocument document in facility.Documents.ToArray())
                    facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, PgNow.AddMinutes(1));
                facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, PgNow.AddMinutes(2), DateOnly.FromDateTime(PgNow));
                setupDb.Facilities.Add(facility);
                await setupDb.SaveChangesAsync();
                facilityIds.Add(facility.Id);
                Assert.Single(await setupDb.Facilities.Where(x => x.Id == facility.Id).ToListAsync());

                CareHomeProfileMedia gallery = Media(revisionId, CareHomeProfileMediaKind.Gallery, 1, "private/care-home-media/fur04-g1.jpg");
                CareHomeProfileMedia cover = Media(revisionId, CareHomeProfileMediaKind.Cover, 0, "private/care-home-media/fur04-cover.jpg");
                setupDb.ProfileMedia.AddRange(gallery, cover);
                await setupDb.SaveChangesAsync();
                galleryId = gallery.Id;
                coverId = cover.Id;
                mediaIds.AddRange([galleryId, coverId]);
            }

            await using var readDb = CreateCareHomesDb(connectionString);
            var sender = new ForwardingSender(CreateIdentityDb(connectionString));
            var detail = await new GetAdminCareHomeApplicationQueryHandler(readDb, sender, CareHomeTestClockHost.Clock())
                .Handle(new GetAdminCareHomeApplicationQuery(facilityIds[0].Value), default);

            Assert.True(detail.IsSuccess);
            Assert.Single(detail.Value.Revisions);
            Assert.Equal(2, detail.Value.Revisions[0].Media.Count);
            Assert.Equal(CareHomeProfileMediaKind.Cover, detail.Value.Revisions[0].Media[0].Kind);
            Assert.Equal(0, detail.Value.Revisions[0].Media[0].Position);
            Assert.Equal($"/api/v1/admin/care-homes/{facilityIds[0].Value:D}/media/{coverId:D}/file",
                detail.Value.Revisions[0].Media[0].Uri);
            Assert.Equal(galleryId, detail.Value.Revisions[0].Media[1].Id);
            await sender.DisposeAsync();

            // Exact owner file-guard shape from CareHomeProfileMediaController.ReadOwner:
            // owned facilities' revision IDs are read server-side, then the
            // media revision is matched client-side (the previous correlated
            // f.Revisions.Any form does not translate on Npgsql).
            await using (var guardDb = CreateCareHomesDb(connectionString))
            {
                CareHomeProfileMedia? item = await guardDb.ProfileMedia.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == coverId);
                Assert.NotNull(item);

                async Task<bool> OwnsRevisionAsync(UserId actor)
                {
                    List<Guid> revisionIds = await guardDb.Facilities.AsNoTracking()
                        .Where(f => f.OwnerUserId == actor)
                        .SelectMany(f => f.Revisions.Select(r => r.Id))
                        .ToListAsync();
                    return revisionIds.Contains(item.ProfileRevisionId);
                }

                async Task<bool> FacilityOwnsRevisionAsync(Guid careHomeId)
                {
                    List<Guid> revisionIds = await guardDb.Facilities.AsNoTracking()
                        .Where(f => f.Id == new CareHomeId(careHomeId))
                        .SelectMany(f => f.Revisions.Select(r => r.Id))
                        .ToListAsync();
                    return revisionIds.Contains(item.ProfileRevisionId);
                }

                async Task<bool> IsApprovedRevisionAsync(Guid careHomeId)
                {
                    Guid? approvedRevisionId = await guardDb.Facilities.AsNoTracking()
                        .Where(f => f.Id == new CareHomeId(careHomeId) && f.Status == CareHomeStatus.Approved)
                        .Select(f => f.ApprovedRevisionId)
                        .SingleOrDefaultAsync();
                    return approvedRevisionId == item.ProfileRevisionId;
                }

                Assert.True(await OwnsRevisionAsync(owner));
                Assert.False(await OwnsRevisionAsync(UserId.New()));

                Assert.True(await FacilityOwnsRevisionAsync(facilityIds[0].Value));
                Assert.False(await FacilityOwnsRevisionAsync(Guid.NewGuid()));

                Assert.True(await IsApprovedRevisionAsync(facilityIds[0].Value));
                Assert.False(await IsApprovedRevisionAsync(Guid.NewGuid()));
            }

            // A newer submitted draft moves the facility to PendingReview while
            // ApprovedRevisionId still points at the old revision: public
            // discovery stays denied for both the approved and draft media.
            await using (var amendDb = CreateCareHomesDb(connectionString))
            {
                CareHomeFacility amendable = await amendDb.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
                    .SingleAsync(x => x.Id == facilityIds[0]);
                amendable.SaveDraft(owner, amendable.Version, CareHomeFacilityTests.Draft() with { Address = "Pending" }, PgNow.AddMinutes(10));
                foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
                    amendable.UploadDocument(owner, type, $"private/fur04-pending-{type}.pdf", "application/pdf", 100, null, PgNow.AddMinutes(10));
                amendable.Submit(owner, amendable.Version, PgNow.AddMinutes(11));
                Guid draftRevisionId = amendable.SubmittedRevisionId!.Value;
                CareHomeProfileMedia draftMedia = Media(draftRevisionId, CareHomeProfileMediaKind.Cover, 0, "private/care-home-media/fur04-draft.jpg");
                amendDb.ProfileMedia.Add(draftMedia);
                await amendDb.SaveChangesAsync();
                mediaIds.Add(draftMedia.Id);

                async Task<bool> IsApprovedRevisionAsync(Guid careHomeId, Guid revisionId)
                {
                    Guid? approvedRevisionId = await amendDb.Facilities.AsNoTracking()
                        .Where(f => f.Id == new CareHomeId(careHomeId) && f.Status == CareHomeStatus.Approved)
                        .Select(f => f.ApprovedRevisionId)
                        .SingleOrDefaultAsync();
                    return approvedRevisionId == revisionId;
                }

                Assert.False(await IsApprovedRevisionAsync(facilityIds[0].Value, coverId));
                Assert.False(await IsApprovedRevisionAsync(facilityIds[0].Value, draftMedia.Id));
            }
        }
        finally
        {
            await CleanupAsync(connectionString, facilityIds, mediaIds, [], [], []);
        }
    }

    [Fur04PostgresFact]
    public async Task OwnerOperationalBooking_ReturnsStoredSnapshots()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        UserId owner = UserId.New();
        List<CareHomeId> facilityIds = [];
        List<Guid> bookingIds = [];
        try
        {
            Guid bookingId;
            await using (var setupDb = CreateCareHomesDb(connectionString))
            {
                CareHomeFacility facility = NewDraftFacility(owner);
                setupDb.Facilities.Add(facility);
                await setupDb.SaveChangesAsync();
                facilityIds.Add(facility.Id);

                CareHomeBooking booking = CreateBooking(facility.Id, " Resident Arabic ", " Resident English ", 77);
                setupDb.Bookings.Add(booking);
                await setupDb.SaveChangesAsync();
                bookingId = booking.Id;
                bookingIds.Add(bookingId);
                Assert.Equal(1, await setupDb.Bookings.CountAsync(x => x.Id == bookingId));
            }

            await using var readDb = CreateCareHomesDb(connectionString);
            var read = await new OwnerCareHomeBookingOperationalHandler(readDb)
                .Handle(new GetOwnerCareHomeBookingOperationalQuery(owner, bookingId, PgNow), default);

            Assert.True(read.IsSuccess);
            Assert.Equal(" Resident Arabic ", read.Value.ResidentArabicName);
            Assert.Equal(" Resident English ", read.Value.ResidentEnglishName);
            Assert.Equal(77, read.Value.ResidentAge);

            var foreign = await new OwnerCareHomeBookingOperationalHandler(readDb)
                .Handle(new GetOwnerCareHomeBookingOperationalQuery(UserId.New(), bookingId, PgNow), default);
            Assert.Equal("CareHomes.Bookings.NotFound", foreign.Error.Code);
        }
        finally
        {
            await CleanupAsync(connectionString, facilityIds, [], bookingIds, [], []);
        }
    }

    [Fur04PostgresFact]
    public async Task AdminDisputes_ReturnStoredSnapshotsAndOriginalAmount()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        List<CareHomeId> facilityIds = [];
        List<Guid> bookingIds = [];
        List<Guid> disputeIds = [];
        try
        {
            await using (var setupDb = CreateCareHomesDb(connectionString))
            {
                CareHomeFacility facility = CareHomeFacility.CreateDraft(UserId.New(), PgNow);
                setupDb.Facilities.Add(facility);
                await setupDb.SaveChangesAsync();
                facilityIds.Add(facility.Id);

                CareHomeBooking booking = CreateBooking(facility.Id, "مقيم", "Resident", 80);
                booking.MarkPaid(123, PgNow.AddMinutes(1));
                booking.Accept(PgNow.AddMinutes(2));
                booking.AssignPhysicalResource(Guid.NewGuid(), null, PgNow.AddMinutes(3));
                booking.RecordCheckIn(UserId.New(), PgNow.AddMinutes(4));
                booking.RecordCheckOut(UserId.New(), PgNow.AddMinutes(5));
                setupDb.Bookings.Add(booking);
                await setupDb.SaveChangesAsync();
                bookingIds.Add(booking.Id);

                CareHomeCheckInDispute dispute = CareHomeCheckInDispute.Open(
                    booking.Id, facility.Id, booking.ActualCheckOutOnUtc, UserId.New(), PgNow.AddMinutes(6), "Issue");
                setupDb.CheckInDisputes.Add(dispute);
                await setupDb.SaveChangesAsync();
                disputeIds.Add(dispute.Id);
                Assert.Equal(1, await setupDb.CheckInDisputes.CountAsync(x => x.Id == dispute.Id));
            }

            await using var readDb = CreateCareHomesDb(connectionString);
            var list = await new CheckInDisputeHandlers(readDb)
                .Handle(new ListCareHomeCheckInDisputesQuery(), default);

            Assert.True(list.IsSuccess);
            Assert.Contains(list.Value, x => x.Id == disputeIds[0]);
            AdminCareHomeCheckInDispute item = list.Value.Single(x => x.Id == disputeIds[0]);
            Assert.Equal("مقيم", item.ResidentArabicName);
            Assert.Equal("Resident", item.ResidentEnglishName);
            Assert.Equal(1150m, item.OriginalBookingTotalAmount);
            Assert.Equal("EGP", item.OriginalBookingCurrency);
            Assert.Equal(CareHomeCheckInDisputeStatus.Open, item.Status);
        }
        finally
        {
            await CleanupAsync(connectionString, facilityIds, [], bookingIds, disputeIds, []);
        }
    }

    [Fur04PostgresFact]
    public async Task AdminApplication_IdentityEnrichment_RolesAreCurrentAndAnonymizedIsNull()
    {
        string connectionString = GetValidatedConnectionString();
        await EnsureMigratedAsync(connectionString);

        List<CareHomeId> facilityIds = [];
        List<UserId> userIds = [];
        try
        {
            Guid ownerId;
            Guid reviewerId;
            Guid anonId;
            await using (var identityDb = CreateIdentityDb(connectionString))
            {
                User ownerUser = IdentityUser("مالك الدار", "Facility Owner", AccountType.CareHomeOwner);
                User reviewerUser = IdentityUser("مراجع الدار", "Home Reviewer", AccountType.SupportAdmin);
                User anonUser = IdentityUser("مستخدم", "Soon Deleted", AccountType.SupportAdmin);
                identityDb.Users.AddRange(ownerUser, reviewerUser, anonUser);
                await identityDb.SaveChangesAsync();
                ownerId = ownerUser.Id.Value;
                reviewerId = reviewerUser.Id.Value;
                anonId = anonUser.Id.Value;
                userIds.AddRange([ownerUser.Id, reviewerUser.Id, anonUser.Id]);
                Assert.Equal(3, await identityDb.Users.CountAsync(x => x.Id == ownerUser.Id || x.Id == reviewerUser.Id || x.Id == anonUser.Id));

                anonUser.AnonymizeAndDeactivate("FUR-04 synthetic fixture anonymized.", PgNow);
                await identityDb.SaveChangesAsync();
            }

            await using (var setupDb = CreateCareHomesDb(connectionString))
            {
                CareHomeFacility facility = NewDraftFacility(new UserId(ownerId));
                AddRequiredDocuments(facility, new UserId(ownerId));
                facility.Submit(new UserId(ownerId), facility.Version, PgNow);
                foreach (CareHomeDocument document in facility.Documents.ToArray())
                    facility.VerifyDocument(new UserId(reviewerId), facility.Version, document.Id, null, true, PgNow.AddMinutes(1));
                facility.Review(new UserId(reviewerId), facility.Version, CareHomeReviewAction.Approved, null, PgNow.AddMinutes(2), DateOnly.FromDateTime(PgNow));
                facility.Review(new UserId(anonId), facility.Version, CareHomeReviewAction.Suspended, "License alert", PgNow.AddMinutes(3), DateOnly.FromDateTime(PgNow));
                setupDb.Facilities.Add(facility);
                await setupDb.SaveChangesAsync();
                facilityIds.Add(facility.Id);
            }

            await using var readDb = CreateCareHomesDb(connectionString);
            await using var sender = new ForwardingSender(CreateIdentityDb(connectionString));
            var before = await new GetAdminCareHomeApplicationQueryHandler(readDb, sender, CareHomeTestClockHost.Clock())
                .Handle(new GetAdminCareHomeApplicationQuery(facilityIds[0].Value), default);

            Assert.True(before.IsSuccess);
            Assert.Equal("مالك الدار", before.Value.OwnerArabicName);
            Assert.Equal("Facility Owner", before.Value.OwnerEnglishName);
            CareHomeAdminReviewHistory approval = before.Value.ReviewHistory.Single(x => x.ActorUserId == reviewerId);
            Assert.Equal("مراجع الدار", approval.ActorArabicName);
            Assert.Equal(["SupportAdmin"], approval.ActorCurrentRoles);
            CareHomeAdminReviewHistory suspension = before.Value.ReviewHistory.Single(x => x.ActorUserId == anonId);
            Assert.Null(suspension.ActorArabicName);
            Assert.Null(suspension.ActorEnglishName);
            Assert.Null(suspension.ActorCurrentRoles);

            await using (var identityDb = CreateIdentityDb(connectionString))
            {
                User liveReviewer = await identityDb.Users.Include(x => x.Accounts)
                    .SingleAsync(x => x.Id == new UserId(reviewerId));
                liveReviewer.ChangeAdministrativeAccount(AccountType.SuperAdmin, PgNow.AddMinutes(9));
                await identityDb.SaveChangesAsync();
            }

            var after = await new GetAdminCareHomeApplicationQueryHandler(readDb, sender, CareHomeTestClockHost.Clock())
                .Handle(new GetAdminCareHomeApplicationQuery(facilityIds[0].Value), default);
            Assert.True(after.IsSuccess);
            Assert.Equal(["SuperAdmin"], after.Value.ReviewHistory.Single(x => x.ActorUserId == reviewerId).ActorCurrentRoles);
        }
        finally
        {
            await CleanupAsync(connectionString, facilityIds, [], [], [], userIds);
        }
    }

    private static async Task EnsureMigratedAsync(string connectionString)
    {
        if (_migratedFor == connectionString) return;
        await MigrationLock.WaitAsync();
        try
        {
            if (_migratedFor == connectionString) return;
            await using var careHomes = CreateCareHomesDb(connectionString);
            await careHomes.Database.MigrateAsync();
            await using var identity = CreateIdentityDb(connectionString);
            await identity.Database.MigrateAsync();
            _migratedFor = connectionString;
        }
        finally
        {
            MigrationLock.Release();
        }
    }

    private static async Task CleanupAsync(
        string connectionString,
        List<CareHomeId> facilityIds,
        List<Guid> mediaIds,
        List<Guid> bookingIds,
        List<Guid> disputeIds,
        List<UserId> userIds)
    {
        await using var careHomes = CreateCareHomesDb(connectionString);
        if (disputeIds.Count > 0)
            await careHomes.CheckInDisputes.Where(x => disputeIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (bookingIds.Count > 0)
            await careHomes.Bookings.Where(x => bookingIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (mediaIds.Count > 0)
            await careHomes.ProfileMedia.Where(x => mediaIds.Contains(x.Id)).ExecuteDeleteAsync();
        if (facilityIds.Count > 0)
            await careHomes.Facilities.Where(x => facilityIds.Contains(x.Id)).ExecuteDeleteAsync();

        if (userIds.Count > 0)
        {
            await using var identity = CreateIdentityDb(connectionString);
            List<User> users = await identity.Users.Include(x => x.Accounts)
                .Where(x => userIds.Contains(x.Id)).ToListAsync();
            identity.Users.RemoveRange(users);
            await identity.SaveChangesAsync();
        }
    }

    private static CareHomeFacility NewDraftFacility(UserId owner)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, PgNow);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), PgNow);
        return facility;
    }

    private static void AddRequiredDocuments(CareHomeFacility facility, UserId owner)
    {
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/fur04-{type}.pdf", "application/pdf", 100, null, PgNow);
    }

    private static CareHomeProfileMedia Media(Guid revisionId, CareHomeProfileMediaKind kind, int position, string key) =>
        CareHomeProfileMedia.Create(revisionId, kind, position, key, "image/jpeg", 100, PgNow);

    private static CareHomeBooking CreateBooking(CareHomeId facility, string arabic, string english, int age) =>
        CareHomeBooking.Create(facility, UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 20), 1000m, 100m, 50m, 4, arabic, english, age,
            null, "Contact", null, null, null, PgNow);

    private static User IdentityUser(string arabic, string english, AccountType accountType)
    {
        int seq = Interlocked.Increment(ref _phoneSeq);
        User user = User.Create(
            FullName.Create(arabic),
            FullName.Create(english),
            Email.Create($"fur04-{seq}@example.test"),
            PhoneNumber.Create($"+20100555{seq:D4}"),
            null);
        user.AddAccount(accountType);
        return user;
    }

    private static string GetValidatedConnectionString()
    {
        string? raw = Environment.GetEnvironmentVariable(ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(raw))
            throw new InvalidOperationException($"Test requires {ConnectionStringVariable}.");

        var builder = new NpgsqlConnectionStringBuilder(raw);
        if (builder.Host is not ("localhost" or "127.0.0.1" or "::1"))
            throw new InvalidOperationException(
                "Refusing to write: the FUR-04 verification target " +
                "must be a local disposable PostgreSQL instance.");
        if (!string.Equals(builder.Database, ExpectedDatabase, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Refusing to write: the FUR-04 verification target " +
                $"must be database '{ExpectedDatabase}'.");
        return builder.ConnectionString;
    }

    private static CareHomesDbContext CreateCareHomesDb(string connectionString)
    {
        DbContextOptions<CareHomesDbContext> options =
            new DbContextOptionsBuilder<CareHomesDbContext>()
                .UseNpgsql(connectionString).Options;
        return new CareHomesDbContext(options);
    }

    private static IdentityDbContext CreateIdentityDb(string connectionString)
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseNpgsql(connectionString).Options;
        return new IdentityDbContext(options);
    }

    private sealed class ForwardingSender(IdentityDbContext identity) : ISender, IAsyncDisposable
    {
        private readonly IdentityDbContext _identity = identity;

        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetUserDisplayInfoByIdsQuery query)
            {
                var result = await new GetUserDisplayInfoByIdsQueryHandler(_identity)
                    .Handle(query, cancellationToken);
                return (TResponse)(object)result;
            }
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public async ValueTask DisposeAsync() => await _identity.DisposeAsync();
    }

    private sealed class Fur04PostgresFactAttribute : FactAttribute
    {
        public Fur04PostgresFactAttribute()
        {
            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(ConnectionStringVariable)))
            {
                Skip = "FUR-04 PostgreSQL proof is skipped. Set " +
                       "ConnectionStrings__Fur04VerificationDatabase " +
                       "to a validated disposable target to run it.";
            }
        }
    }
}

[CollectionDefinition("Fur04Verification", DisableParallelization = true)]
public sealed class Fur04VerificationCollection
{
}
