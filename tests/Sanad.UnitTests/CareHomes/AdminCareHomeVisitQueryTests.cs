using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Visits;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// F6 minimal operational Care Home visit read. Every case runs against a
/// relational (SQLite) Care Homes store on purpose: the facility/status/window
/// filters, the converted-id ordering, the batched facility/family name
/// lookups and the nullable-id projection are the parts a relational
/// translation can reject. The projection is asserted to publish only the
/// pinned visit facts — the facility's approved display name and the family's
/// name are included, while visitor name, visitor phone, visitor count and the
/// requesting family user id are never returned, and there is no Admin
/// decision, scheduling or cancellation writer anywhere in this read.
/// </summary>
public sealed class AdminCareHomeVisitQueryTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task List_orders_newest_first_and_clamps_paging()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        CareHomeId facility = await db.AddFacilityAsync();
        CareHomeVisit oldest = Visit(facility, new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc));
        CareHomeVisit middle = Visit(facility, new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc));
        CareHomeVisit newest = Visit(facility, new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc));
        db.Context.Visits.AddRange(oldest, middle, newest);
        await db.Context.SaveChangesAsync();

        var handler = new ListAdminCareHomeVisitsQueryHandler(db.Context, db.Families);

        var all = await handler.Handle(new(), default);
        var clamped = await handler.Handle(new(Page: 0, PageSize: 0), default);
        var oversized = await handler.Handle(new(PageSize: 500), default);
        var secondPage = await handler.Handle(new(Page: 2, PageSize: 2), default);
        var beyond = await handler.Handle(new(Page: 3, PageSize: 2), default);

        Assert.True(all.IsSuccess);
        Assert.Equal([newest.Id, middle.Id, oldest.Id], all.Value.Items.Select(item => item.VisitId).ToArray());
        Assert.Equal(3, all.Value.TotalCount);
        Assert.Equal(1, all.Value.Page);
        Assert.Equal(25, all.Value.PageSize);
        Assert.Equal(1, clamped.Value.Page);
        Assert.Equal(25, clamped.Value.PageSize);
        Assert.Equal(100, oversized.Value.PageSize);
        Assert.Equal(oldest.Id, Assert.Single(secondPage.Value.Items).VisitId);
        Assert.Empty(beyond.Value.Items);
        Assert.Equal(3, beyond.Value.TotalCount);
    }

    [Fact]
    public async Task List_filters_by_facility_status_and_the_inclusive_utc_day_window()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        CareHomeId facilityA = await db.AddFacilityAsync();
        CareHomeId facilityB = await db.AddFacilityAsync();
        CareHomeVisit touchingWindow = Visit(facilityA, new DateTime(2026, 10, 19, 23, 30, 0, DateTimeKind.Utc));
        CareHomeVisit lateInWindow = Visit(facilityA, new DateTime(2026, 10, 20, 23, 0, 0, DateTimeKind.Utc));
        CareHomeVisit afterWindow = Visit(facilityA, new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc));
        CareHomeVisit beforeWindow = Visit(facilityA, new DateTime(2026, 10, 18, 9, 0, 0, DateTimeKind.Utc));
        CareHomeVisit otherFacility = Visit(facilityB, new DateTime(2026, 10, 20, 9, 0, 0, DateTimeKind.Utc));
        db.Context.Visits.AddRange(touchingWindow, lateInWindow, afterWindow, beforeWindow, otherFacility);
        await db.Context.SaveChangesAsync();

        touchingWindow.Approve(UserId.New(), Now.AddMinutes(1));
        await db.Context.SaveChangesAsync();

        var handler = new ListAdminCareHomeVisitsQueryHandler(db.Context, db.Families);
        var window = await handler.Handle(
            new(From: new DateOnly(2026, 10, 20), To: new DateOnly(2026, 10, 20)), default);
        var facilityPage = await handler.Handle(new(FacilityId: facilityA.Value), default);
        var windowInFacility = await handler.Handle(
            new(FacilityId: facilityA.Value, From: new DateOnly(2026, 10, 20), To: new DateOnly(2026, 10, 20)),
            default);
        var approved = await handler.Handle(new(Status: CareHomeVisitStatus.Approved), default);
        var pending = await handler.Handle(new(Status: CareHomeVisitStatus.Pending), default);

        Assert.True(window.IsSuccess);
        Assert.Equal(3, window.Value.TotalCount);
        Assert.Equal(
            [lateInWindow.Id, otherFacility.Id, touchingWindow.Id],
            window.Value.Items.Select(item => item.VisitId).ToArray());

        Assert.Equal(4, facilityPage.Value.TotalCount);
        Assert.DoesNotContain(otherFacility.Id, facilityPage.Value.Items.Select(item => item.VisitId));

        Assert.Equal(2, windowInFacility.Value.TotalCount);
        Assert.Equal(
            [lateInWindow.Id, touchingWindow.Id],
            windowInFacility.Value.Items.Select(item => item.VisitId).ToArray());

        Assert.Equal(touchingWindow.Id, Assert.Single(approved.Value.Items).VisitId);
        Assert.Equal(4, pending.Value.TotalCount);
        Assert.DoesNotContain(touchingWindow.Id, pending.Value.Items.Select(item => item.VisitId));
    }

    [Fact]
    public async Task List_rejects_an_empty_facility_an_undefined_status_and_an_inverted_window()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        var handler = new ListAdminCareHomeVisitsQueryHandler(db.Context, db.Families);

        var emptyFacility = await handler.Handle(new(FacilityId: Guid.Empty), default);
        var undefinedStatus = await handler.Handle(new(Status: (CareHomeVisitStatus)999), default);
        var invertedWindow = await handler.Handle(
            new(From: new DateOnly(2026, 10, 20), To: new DateOnly(2026, 10, 19)), default);

        foreach (var rejected in new[] { emptyFacility, undefinedStatus, invertedWindow })
        {
            Assert.False(rejected.IsSuccess);
            Assert.Equal("CareHomes.Admin.InvalidQuery", rejected.Error.Code);
        }
    }

    [Fact]
    public async Task Detail_publishes_the_recorded_decision_cancellation_and_reschedule_facts()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        CareHomeId facility = await db.AddFacilityAsync();
        UserId decider = UserId.New();
        UserId canceller = UserId.New();
        ElderlyId dependent = ElderlyId.New();

        CareHomeVisit rejected = CareHomeVisit.Request(
            facility, FamilyId.New(), UserId.New(), CareHomeVisitKind.Resident, dependent,
            "SECRET_VISITOR_NAME", "SECRET_VISITOR_PHONE", 4,
            new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc), Now);
        rejected.Reject(decider, "Capacity is exhausted for that window.", Now.AddMinutes(1));

        CareHomeVisit cancelled = CareHomeVisit.Request(
            facility, FamilyId.New(), UserId.New(), CareHomeVisitKind.Prospective, null,
            "Visitor", "01000000001", 2,
            new DateTime(2026, 10, 16, 11, 0, 0, DateTimeKind.Utc), Now);
        cancelled.Cancel(byFacility: true, canceller, "Facility closed that day.", Now.AddMinutes(2));

        CareHomeVisit approved = CareHomeVisit.Request(
            facility, FamilyId.New(), UserId.New(), CareHomeVisitKind.Prospective, null,
            "Visitor", "01000000002", 2,
            new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc), Now);
        approved.Approve(decider, Now.AddMinutes(3));
        CareHomeVisit rescheduled = CareHomeVisit.RequestReschedule(
            approved, new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc), Now.AddMinutes(4));

        db.Context.Visits.AddRange(rejected, cancelled, approved);
        await db.Context.SaveChangesAsync();

        // The reschedule row references its parent through a real self foreign
        // key, so the parent is persisted first.
        db.Context.Visits.Add(rescheduled);
        await db.Context.SaveChangesAsync();

        var handler = new GetAdminCareHomeVisitQueryHandler(db.Context, db.Families);
        var rejectedDetail = await handler.Handle(new(rejected.Id), default);
        var cancelledDetail = await handler.Handle(new(cancelled.Id), default);
        var rescheduledDetail = await handler.Handle(new(rescheduled.Id), default);

        Assert.True(rejectedDetail.IsSuccess);
        Assert.Equal(facility.Value, rejectedDetail.Value.FacilityId);
        Assert.Equal(rejected.FamilyId.Value, rejectedDetail.Value.FamilyId);
        Assert.Equal(dependent.Value, rejectedDetail.Value.ElderlyId);
        Assert.Equal(CareHomeVisitKind.Resident, rejectedDetail.Value.Kind);
        Assert.Equal(CareHomeVisitStatus.Rejected, rejectedDetail.Value.Status);
        Assert.Equal(rejected.StartsAtUtc, rejectedDetail.Value.StartsAtUtc);
        Assert.Equal(rejected.EndsAtUtc, rejectedDetail.Value.EndsAtUtc);
        Assert.Equal(decider.Value, rejectedDetail.Value.DecidedByUserId);
        Assert.Equal(rejected.DecidedOnUtc, rejectedDetail.Value.DecidedOnUtc);
        Assert.Equal("Capacity is exhausted for that window.", rejectedDetail.Value.DecisionReason);
        Assert.Null(rejectedDetail.Value.RescheduleOfVisitId);
        Assert.Null(rejectedDetail.Value.CancelledByUserId);

        Assert.True(cancelledDetail.IsSuccess);
        Assert.Equal(CareHomeVisitStatus.FacilityCancelled, cancelledDetail.Value.Status);
        Assert.Equal(canceller.Value, cancelledDetail.Value.CancelledByUserId);
        Assert.Equal(cancelled.CancelledOnUtc, cancelledDetail.Value.CancelledOnUtc);
        Assert.Equal("Facility closed that day.", cancelledDetail.Value.CancellationReason);

        Assert.True(rescheduledDetail.IsSuccess);
        Assert.Equal(approved.Id, rescheduledDetail.Value.RescheduleOfVisitId);
        Assert.Equal(CareHomeVisitStatus.ReschedulePending, rescheduledDetail.Value.Status);
    }

    [Fact]
    public async Task Detail_never_returns_visitor_contact_data_or_the_requesting_family_user()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        CareHomeId facility = await db.AddFacilityAsync();
        CareHomeVisit visit = CareHomeVisit.Request(
            facility, FamilyId.New(), UserId.New(), CareHomeVisitKind.Prospective, null,
            "SECRET_VISITOR_NAME", "SECRET_VISITOR_PHONE", 7,
            new DateTime(2026, 10, 15, 10, 0, 0, DateTimeKind.Utc), Now);
        db.Context.Visits.Add(visit);
        await db.Context.SaveChangesAsync();

        var detail = await new GetAdminCareHomeVisitQueryHandler(db.Context, db.Families).Handle(new(visit.Id), default);
        var list = await new ListAdminCareHomeVisitsQueryHandler(db.Context, db.Families).Handle(new(), default);

        Assert.True(detail.IsSuccess);
        string detailJson = JsonSerializer.Serialize(detail.Value);
        string listJson = JsonSerializer.Serialize(list.Value.Items);

        foreach (string json in new[] { detailJson, listJson })
        {
            Assert.DoesNotContain("SECRET_VISITOR_NAME", json, StringComparison.Ordinal);
            Assert.DoesNotContain("SECRET_VISITOR_PHONE", json, StringComparison.Ordinal);

            foreach (string forbidden in new[]
                     {
                         "visitorName", "visitorPhone", "visitorCount", "familyUserId",
                         "decisionExpiresOnUtc", "version"
                     })
            {
                Assert.DoesNotContain(forbidden, json, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public async Task Detail_returns_not_found_for_an_unknown_or_empty_visit_id()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        var handler = new GetAdminCareHomeVisitQueryHandler(db.Context, db.Families);

        var unknown = await handler.Handle(new(Guid.NewGuid()), default);
        var empty = await handler.Handle(new(Guid.Empty), default);

        Assert.False(unknown.IsSuccess);
        Assert.Equal("CareHomes.Visits.NotFound", unknown.Error.Code);
        Assert.False(empty.IsSuccess);
        Assert.Equal("CareHomes.Visits.NotFound", empty.Error.Code);
    }

    [Fact]
    public async Task List_and_detail_publish_the_facility_and_family_names()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        CareHomeId facility = await db.AddApprovedFacilityAsync(
            arabicName: "دار الأمل", englishName: "Hope House");
        FamilyId family = await db.AddFamilyAsync("The Nasr Family");
        CareHomeVisit visit = Visit(
            facility, new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc), family);
        db.Context.Visits.Add(visit);
        await db.Context.SaveChangesAsync();

        var list = await new ListAdminCareHomeVisitsQueryHandler(db.Context, db.Families)
            .Handle(new(), default);
        var detail = await new GetAdminCareHomeVisitQueryHandler(db.Context, db.Families)
            .Handle(new(visit.Id), default);

        Assert.True(list.IsSuccess);
        AdminCareHomeVisitListItem row = Assert.Single(list.Value.Items);
        // The approved revision's English name wins over its Arabic name.
        Assert.Equal("Hope House", row.FacilityName);
        Assert.Equal(facility.Value, row.FacilityId);
        Assert.Equal("The Nasr Family", row.FamilyName);
        Assert.Equal(family.Value, row.FamilyId);

        Assert.True(detail.IsSuccess);
        Assert.Equal("Hope House", detail.Value.FacilityName);
        Assert.Equal("The Nasr Family", detail.Value.FamilyName);
    }

    [Fact]
    public async Task Names_degrade_to_null_without_throwing_when_they_cannot_be_resolved()
    {
        await using TestCareHomeVisitDb db = await TestCareHomeVisitDb.CreateAsync();
        // A stored facility that never had an approved revision and a visit
        // whose family has no Families row: both names stay null, the read
        // still succeeds and nothing is invented.
        CareHomeId facility = await db.AddFacilityAsync();
        CareHomeVisit visit = Visit(
            facility, new DateTime(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc));
        db.Context.Visits.Add(visit);
        await db.Context.SaveChangesAsync();

        var list = await new ListAdminCareHomeVisitsQueryHandler(db.Context, db.Families)
            .Handle(new(), default);
        var detail = await new GetAdminCareHomeVisitQueryHandler(db.Context, db.Families)
            .Handle(new(visit.Id), default);

        Assert.True(list.IsSuccess);
        AdminCareHomeVisitListItem row = Assert.Single(list.Value.Items);
        Assert.Equal(facility.Value, row.FacilityId);
        Assert.Null(row.FacilityName);
        Assert.Null(row.FamilyName);

        Assert.True(detail.IsSuccess);
        Assert.Equal(facility.Value, detail.Value.FacilityId);
        Assert.Null(detail.Value.FacilityName);
        Assert.Null(detail.Value.FamilyName);

        // An empty page never queries either name source and still succeeds.
        // The Families half is pinned hard here: this store has no schema at
        // all, so any family-name lookup on it fails instead of quietly
        // returning nothing. The facility half is the very same
        // `distinct.Length == 0` guard in the sibling resolver, which shares
        // the visits store and so cannot be swapped the same way.
        await using var bareFamilies = new FamiliesDbContext(
            new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseSqlite("DataSource=:memory:")
                .Options);
        var empty = await new ListAdminCareHomeVisitsQueryHandler(db.Context, bareFamilies)
            .Handle(new(Status: CareHomeVisitStatus.Approved), default);
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value.Items);
    }

    private static CareHomeVisit Visit(
        CareHomeId facility,
        DateTime startsAtUtc,
        FamilyId? family = null) =>
        CareHomeVisit.Request(
            facility,
            family ?? FamilyId.New(),
            UserId.New(),
            CareHomeVisitKind.Prospective,
            null,
            "Visitor",
            "01000000000",
            2,
            startsAtUtc,
            Now);

    private static CareHomeProfileDraft Draft(string arabicName, string englishName) =>
        new(
            arabicName,
            englishName,
            "رعاية وإقامة",
            "Care and recovery",
            "Contact",
            "+201000000000",
            null,
            "Cairo",
            "Nasr City",
            "District 1",
            "Example address",
            "الشروط",
            "Admission conditions",
            [],
            []);

    private sealed class TestCareHomeVisitDb : IAsyncDisposable
    {
        private readonly SqliteConnection _connection;
        private readonly SqliteConnection _familiesConnection;

        private TestCareHomeVisitDb(
            SqliteConnection connection,
            CareHomesDbContext context,
            SqliteConnection familiesConnection,
            FamiliesDbContext families)
        {
            _connection = connection;
            Context = context;
            _familiesConnection = familiesConnection;
            Families = families;
        }

        public CareHomesDbContext Context { get; }

        /// <summary>
        /// The Families side of the visit read. A real relational Families
        /// store on its own in-memory database, so the family-name lookup is
        /// exercised against the same provider the production read uses
        /// instead of a hand-written double.
        /// </summary>
        public FamiliesDbContext Families { get; }

        public static async Task<TestCareHomeVisitDb> CreateAsync()
        {
            var connection = new SqliteConnection("DataSource=:memory:");
            await connection.OpenAsync();
            var context = new CareHomesDbContext(
                new DbContextOptionsBuilder<CareHomesDbContext>()
                    .UseSqlite(connection)
                    .Options);
            await context.Database.EnsureCreatedAsync();

            var familiesConnection = new SqliteConnection("DataSource=:memory:");
            await familiesConnection.OpenAsync();
            var families = new FamiliesDbContext(
                new DbContextOptionsBuilder<FamiliesDbContext>()
                    .UseSqlite(familiesConnection)
                    .Options);
            await families.Database.EnsureCreatedAsync();

            return new TestCareHomeVisitDb(connection, context, familiesConnection, families);
        }

        /// <summary>
        /// The visit table has a real foreign key to the facility table, so a
        /// stored facility is required before a visit can be persisted.
        /// </summary>
        public async Task<CareHomeId> AddFacilityAsync()
        {
            CareHomeFacility facility = CareHomeFacility.CreateDraft(UserId.New(), Now);
            Context.Facilities.Add(facility);
            await Context.SaveChangesAsync();
            return facility.Id;
        }

        /// <summary>
        /// A facility with an Admin-approved revision, so its approved display
        /// name is resolvable by the visit read.
        /// </summary>
        public async Task<CareHomeId> AddApprovedFacilityAsync(
            string arabicName,
            string englishName)
        {
            UserId owner = UserId.New();
            UserId admin = UserId.New();
            CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
            facility.SaveDraft(owner, facility.Version, Draft(arabicName, englishName), Now);
            foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            {
                facility.UploadDocument(
                    owner, type, $"private/{type}.pdf", "application/pdf", 100, null, Now);
            }

            facility.Submit(owner, facility.Version, Now);
            foreach (CareHomeDocument document in facility.Documents.ToArray())
            {
                facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now);
            }

            facility.Review(
                admin,
                facility.Version,
                CareHomeReviewAction.Approved,
                null,
                Now,
                DateOnly.FromDateTime(Now));

            Context.Facilities.Add(facility);
            await Context.SaveChangesAsync();
            return facility.Id;
        }

        /// <summary>One Families aggregate with the given name.</summary>
        public async Task<FamilyId> AddFamilyAsync(string name)
        {
            Family family = Family.Create(UserId.New(), name);
            Families.Families.Add(family);
            await Families.SaveChangesAsync();
            return family.Id;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await Families.DisposeAsync();
            await _connection.DisposeAsync();
            await _familiesConnection.DisposeAsync();
        }
    }
}
