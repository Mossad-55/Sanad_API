using System.Text.Json;
using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

/// <summary>
/// FUR-04 Care Home readback focused tests. Covers the owner profile
/// revision-bound readbacks, Admin revision media metadata and display-name
/// enrichment, owner operational booking resident snapshots, and Admin
/// dispute booking snapshots. No live database is used here.
/// </summary>
public sealed class CareHomeReadbackTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);

    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);

    private static UserDisplayInfo Display(Guid id, string arabic, string english, params int[] accountTypes) =>
        new(new UserId(id), arabic, english, accountTypes);

    private sealed class DisplayInfoSender(
        IReadOnlyDictionary<Guid, UserDisplayInfo>? infoById = null) : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetUserDisplayInfoByIdsQuery query)
            {
                IReadOnlyList<UserDisplayInfo> result = query.UserIds
                    .Where(id => infoById is not null && infoById.ContainsKey(id.Value))
                    .Select(id => infoById![id.Value])
                    .ToArray();
                return Task.FromResult((TResponse)(object)Result<IReadOnlyList<UserDisplayInfo>>.Success(result));
            }
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Fact]
    public async Task OwnerProfile_Draft_HasDraftOnly_AndGetDoesNotMutate()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var handler = new GetMyCareHomeFacilityQueryHandler(db, CareHomeTestClockHost.Clock());
        var read = await handler.Handle(new GetMyCareHomeFacilityQuery(owner), default);

        Assert.True(read.IsSuccess);
        Assert.Equal(CareHomeStatus.Draft, read.Value.Status);
        Assert.NotNull(read.Value.Draft);
        Assert.Null(read.Value.SubmittedProfile);
        Assert.Null(read.Value.ApprovedProfile);

        int revisionsBefore = facility.Revisions.Count;
        await handler.Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.Equal(revisionsBefore, facility.Revisions.Count);
        Assert.Equal(0, db.ChangeTracker.Entries().Count(e => e.State is EntityState.Added or EntityState.Modified));
    }

    [Fact]
    public async Task OwnerProfile_PendingReview_BindsSubmittedRevision()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var read = await new GetMyCareHomeFacilityQueryHandler(db, CareHomeTestClockHost.Clock())
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);

        Assert.True(read.IsSuccess);
        Assert.Equal(CareHomeStatus.PendingReview, read.Value.Status);
        Assert.NotNull(read.Value.SubmittedProfile);
        Assert.Equal(facility.SubmittedRevisionId, read.Value.SubmittedProfile!.RevisionId);
        Assert.Equal("Hope House", read.Value.SubmittedProfile.Profile.EnglishName);
        Assert.Null(read.Value.ApprovedProfile);
    }

    [Fact]
    public async Task OwnerProfile_NeedsCorrectionAndRejected_ClearSubmittedRevision()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        Guid submittedId = facility.SubmittedRevisionId!.Value;

        facility.Review(admin, facility.Version, CareHomeReviewAction.CorrectionsRequested, "Fix it", Now.AddMinutes(1), DateOnly.FromDateTime(Now));
        await db.SaveChangesAsync();

        var needsCorrection = await new GetMyCareHomeFacilityQueryHandler(db, CareHomeTestClockHost.Clock())
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.True(needsCorrection.IsSuccess);
        Assert.Equal(CareHomeStatus.NeedsCorrection, needsCorrection.Value.Status);
        Assert.Null(needsCorrection.Value.SubmittedProfile);

        facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents).SingleAsync(x => x.OwnerUserId == owner);
        facility.SaveDraft(owner, facility.Version, CareHomeFacilityTests.Draft(), Now.AddMinutes(2));
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now.AddMinutes(2));
        await db.SaveChangesAsync();
        Assert.NotEqual(submittedId, facility.SubmittedRevisionId!.Value);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Rejected, "Rejected", Now.AddMinutes(3), DateOnly.FromDateTime(Now));
        await db.SaveChangesAsync();

        var rejected = await new GetMyCareHomeFacilityQueryHandler(db, CareHomeTestClockHost.Clock())
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.True(rejected.IsSuccess);
        Assert.Equal(CareHomeStatus.Rejected, rejected.Value.Status);
        Assert.Null(rejected.Value.SubmittedProfile);
        Assert.Null(rejected.Value.ApprovedProfile);
    }

    [Fact]
    public async Task OwnerProfile_Approved_BindsApprovedRevision_AndKeepsNewerDraft()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, Now.AddMinutes(1));
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(2), DateOnly.FromDateTime(Now));
        Guid approvedRevisionId = facility.ApprovedRevisionId!.Value;
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var approved = await new GetMyCareHomeFacilityQueryHandler(db, CareHomeTestClockHost.Clock())
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.True(approved.IsSuccess);
        Assert.Equal(CareHomeStatus.Approved, approved.Value.Status);
        Assert.Null(approved.Value.SubmittedProfile);
        Assert.NotNull(approved.Value.ApprovedProfile);
        Assert.Equal(approvedRevisionId, approved.Value.ApprovedProfile!.RevisionId);

        CareHomeFacility editable = await db.Facilities.Include(x => x.Revisions).SingleAsync(x => x.OwnerUserId == owner);
        editable.SaveDraft(owner, editable.Version, CareHomeFacilityTests.Draft() with { Address = "Updated address" }, Now.AddMinutes(3));
        await db.SaveChangesAsync();

        var approvedWithDraft = await new GetMyCareHomeFacilityQueryHandler(db, CareHomeTestClockHost.Clock())
            .Handle(new GetMyCareHomeFacilityQuery(owner), default);
        Assert.True(approvedWithDraft.IsSuccess);
        Assert.Equal(CareHomeStatus.Approved, approvedWithDraft.Value.Status);
        Assert.NotNull(approvedWithDraft.Value.Draft);
        Assert.Equal("Updated address", approvedWithDraft.Value.Draft!.Address);
        Assert.Null(approvedWithDraft.Value.SubmittedProfile);
        Assert.NotNull(approvedWithDraft.Value.ApprovedProfile);
        Assert.Equal(approvedRevisionId, approvedWithDraft.Value.ApprovedProfile!.RevisionId);
    }

    [Fact]
    public async Task AdminApplication_IncludesOrderedRevisionMedia_OwnerNames_AndActorCurrentRoles()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        CareHomeProfileRevision first = facility.Revisions.Single();
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(1));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(2), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        db.ProfileMedia.AddRange(
            Media(first.Id, CareHomeProfileMediaKind.Gallery, 1, "private/care-home-media/first-g1.jpg"),
            Media(first.Id, CareHomeProfileMediaKind.Cover, 0, "private/care-home-media/first-cover.jpg"));
        await db.SaveChangesAsync();

        Guid ownerId = owner.Value;
        Guid reviewerId = reviewer.Value;
        var sender = new DisplayInfoSender(new Dictionary<Guid, UserDisplayInfo>
        {
            [ownerId] = Display(ownerId, "صاحب الدار", "Owner Name", (int)AccountType.CareHomeOwner),
            [reviewerId] = Display(reviewerId, "مراجع", "Reviewer Name", (int)AccountType.SupportAdmin)
        });

        var detail = await new GetAdminCareHomeApplicationQueryHandler(db, sender, CareHomeTestClockHost.Clock())
            .Handle(new GetAdminCareHomeApplicationQuery(facility.Id.Value), default);

        Assert.True(detail.IsSuccess);
        Assert.Equal("صاحب الدار", detail.Value.OwnerArabicName);
        Assert.Equal("Owner Name", detail.Value.OwnerEnglishName);
        Assert.Single(detail.Value.Revisions);
        Assert.Equal(2, detail.Value.Revisions[0].Media.Count);
        Assert.Equal(0, detail.Value.Revisions[0].Media[0].Position);
        Assert.Equal(CareHomeProfileMediaKind.Cover, detail.Value.Revisions[0].Media[0].Kind);
        Assert.Equal($"/api/v1/admin/care-homes/{facility.Id.Value:D}/media/{detail.Value.Revisions[0].Media[0].Id:D}/file",
            detail.Value.Revisions[0].Media[0].Uri);
        Assert.Contains(detail.Value.ReviewHistory, h => h.ActorUserId == reviewerId);
        CareHomeAdminReviewHistory actor = detail.Value.ReviewHistory.Single();
        Assert.Equal("مراجع", actor.ActorArabicName);
        Assert.Equal("Reviewer Name", actor.ActorEnglishName);
        Assert.Equal(["SupportAdmin"], actor.ActorCurrentRoles);

        string json = JsonSerializer.Serialize(detail.Value);
        Assert.DoesNotContain("private/care-home-media/first-cover.jpg", json, StringComparison.Ordinal);
        Assert.DoesNotContain("private/care-home-media/first-g1.jpg", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminApplication_MissingOwnerAndActor_ProduceNullEnrichment_AndNoPhone()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(1));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(2), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var detail = await new GetAdminCareHomeApplicationQueryHandler(db, new DisplayInfoSender(), CareHomeTestClockHost.Clock())
            .Handle(new GetAdminCareHomeApplicationQuery(facility.Id.Value), default);

        Assert.True(detail.IsSuccess);
        Assert.Null(detail.Value.OwnerArabicName);
        Assert.Null(detail.Value.OwnerEnglishName);
        Assert.Equal(owner.Value, detail.Value.OwnerUserId);
        Assert.Null(detail.Value.ReviewHistory.Single().ActorArabicName);
        Assert.Null(detail.Value.ReviewHistory.Single().ActorEnglishName);
        Assert.Null(detail.Value.ReviewHistory.Single().ActorCurrentRoles);
    }

    [Fact]
    public async Task AdminApplication_ActorCurrentRoles_ReflectCurrentAccount_NotHistorical()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId reviewer = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(reviewer, facility.Version, document.Id, null, true, Now.AddMinutes(1));
        facility.Review(reviewer, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(2), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        Guid reviewerId = reviewer.Value;
        var sender = new DisplayInfoSender(new Dictionary<Guid, UserDisplayInfo>
        {
            [reviewerId] = Display(reviewerId, "مراجع", "Reviewer Name",
                (int)AccountType.SuperAdmin, (int)AccountType.SupportAdmin)
        });

        var detail = await new GetAdminCareHomeApplicationQueryHandler(db, sender, CareHomeTestClockHost.Clock())
            .Handle(new GetAdminCareHomeApplicationQuery(facility.Id.Value), default);

        Assert.True(detail.IsSuccess);
        Assert.Equal(["SuperAdmin", "SupportAdmin"], detail.Value.ReviewHistory.Single().ActorCurrentRoles);
        Assert.Contains(detail.Value.ReviewHistory, h => h.ActorUserId == reviewerId);
    }

    [Fact]
    public async Task AdminApplication_ListIncludesNullableOwnerNames()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        AddRequiredDocuments(facility, owner);
        facility.Submit(owner, facility.Version, Now);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        Guid ownerId = owner.Value;
        var sender = new DisplayInfoSender(new Dictionary<Guid, UserDisplayInfo>
        {
            [ownerId] = Display(ownerId, "صاحب", "Owner")
        });
        var list = await new GetAdminCareHomeApplicationsQueryHandler(db, sender)
            .Handle(new GetAdminCareHomeApplicationsQuery(), default);

        Assert.True(list.IsSuccess);
        Assert.Single(list.Value.Items);
        Assert.Equal("صاحب", list.Value.Items[0].OwnerArabicName);
        Assert.Equal("Owner", list.Value.Items[0].OwnerEnglishName);
        Assert.Equal(ownerId, list.Value.Items[0].OwnerUserId);
    }

    [Fact]
    public async Task OwnerOperationalBooking_ReturnsStoredResidentSnapshots_AndOwnFacilityScope()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = NewDraftFacility(owner);
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();
        CareHomeBooking booking = CreateBooking(facility.Id, " Resident Arabic ", " Resident English ", 77);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var handler = new OwnerCareHomeBookingOperationalHandler(db);
        var read = await handler.Handle(
            new GetOwnerCareHomeBookingOperationalQuery(owner, booking.Id, Now), default);

        Assert.True(read.IsSuccess);
        Assert.Equal(" Resident Arabic ", read.Value.ResidentArabicName);
        Assert.Equal(" Resident English ", read.Value.ResidentEnglishName);
        Assert.Equal(77, read.Value.ResidentAge);

        var missing = await handler.Handle(new GetOwnerCareHomeBookingOperationalQuery(owner, Guid.NewGuid(), Now), default);
        Assert.Equal("CareHomes.Bookings.NotFound", missing.Error.Code);

        var foreignOwner = await handler.Handle(new GetOwnerCareHomeBookingOperationalQuery(UserId.New(), booking.Id, Now), default);
        Assert.Equal("CareHomes.Bookings.NotFound", foreignOwner.Error.Code);
    }

    [Fact]
    public async Task AdminDisputes_IncludeStoredResidentSnapshots_AndOriginalBookingAmountCurrency()
    {
        using CareHomesDbContext db = CreateDb();
        CareHomeId facility = CareHomeId.New();
        CareHomeBooking booking = CreateBooking(facility, "مقيم", "Resident", 80);
        booking.MarkPaid(123, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        booking.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(3));
        booking.RecordCheckIn(UserId.New(), Now.AddMinutes(4));
        booking.RecordCheckOut(UserId.New(), Now.AddMinutes(5));
        db.Bookings.Add(booking);
        var dispute = CareHomeCheckInDispute.Open(booking.Id, facility, booking.ActualCheckOutOnUtc, UserId.New(), Now.AddMinutes(5), "Issue");
        db.CheckInDisputes.Add(dispute);
        await db.SaveChangesAsync();

        var list = await new CheckInDisputeHandlers(db)
            .Handle(new ListCareHomeCheckInDisputesQuery(), default);

        Assert.True(list.IsSuccess);
        Assert.Single(list.Value);
        AdminCareHomeCheckInDispute item = list.Value[0];
        Assert.Equal("مقيم", item.ResidentArabicName);
        Assert.Equal("Resident", item.ResidentEnglishName);
        Assert.Equal(1150m, item.OriginalBookingTotalAmount);
        Assert.Equal("EGP", item.OriginalBookingCurrency);
        Assert.Equal(CareHomeCheckInDisputeStatus.Open, item.Status);
    }

    [Fact]
    public async Task AdminDisputes_MissingBookingYieldsNullEnrichment()
    {
        using CareHomesDbContext db = CreateDb();
        CareHomeId facility = CareHomeId.New();
        var dispute = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility, null, UserId.New(), Now, "Issue");
        db.CheckInDisputes.Add(dispute);
        await db.SaveChangesAsync();

        var list = await new CheckInDisputeHandlers(db)
            .Handle(new ListCareHomeCheckInDisputesQuery(), default);

        Assert.True(list.IsSuccess);
        Assert.Single(list.Value);
        Assert.Null(list.Value[0].ResidentArabicName);
        Assert.Null(list.Value[0].ResidentEnglishName);
        Assert.Null(list.Value[0].OriginalBookingTotalAmount);
        Assert.Null(list.Value[0].OriginalBookingCurrency);
    }

    private sealed class ForwardingSender(IdentityDbContext identity) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is GetUserDisplayInfoByIdsQuery query)
            {
                var result = await new GetUserDisplayInfoByIdsQueryHandler(identity)
                    .Handle(query, cancellationToken);
                return (TResponse)(object)result;
            }
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminApplication_RealIdentityLookup_EnrichesOwnerAndActor_AnonymizedAndMissingYieldNull(bool sqlite)
    {
        await using var contexts = await DualContexts.CreateAsync(sqlite);
        IdentityDbContext identity = contexts.Identity;
        CareHomesDbContext db = contexts.CareHomes;

        User ownerUser = IdentityUser("+201000000101", "owner@example.test", "مالك الدار", "Facility Owner");
        ownerUser.AddAccount(AccountType.CareHomeOwner);
        User actorUser = IdentityUser("+201000000102", "actor@example.test", "مراجع الدار", "Home Reviewer");
        actorUser.AddAccount(AccountType.SupportAdmin);
        User anonUser = IdentityUser("+201000000103", "anon@example.test", "مستخدم", "Soon Deleted");
        anonUser.AddAccount(AccountType.SupportAdmin);
        identity.Users.AddRange(ownerUser, actorUser, anonUser);
        await identity.SaveChangesAsync();
        anonUser.AnonymizeAndDeactivate(Now);
        await identity.SaveChangesAsync();

        CareHomeFacility facility = NewDraftFacility(ownerUser.Id);
        AddRequiredDocuments(facility, ownerUser.Id);
        facility.Submit(ownerUser.Id, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(actorUser.Id, facility.Version, document.Id, null, true, Now.AddMinutes(1));
        facility.Review(actorUser.Id, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(2), DateOnly.FromDateTime(Now));
        facility.Review(anonUser.Id, facility.Version, CareHomeReviewAction.Suspended, "License alert", Now.AddMinutes(3), DateOnly.FromDateTime(Now));
        UserId missingReviewer = UserId.New();
        facility.Review(missingReviewer, facility.Version, CareHomeReviewAction.Reactivated, null, Now.AddMinutes(4), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var handler = new GetAdminCareHomeApplicationQueryHandler(db, new ForwardingSender(identity), CareHomeTestClockHost.Clock());
        var detail = await handler.Handle(new GetAdminCareHomeApplicationQuery(facility.Id.Value), default);

        Assert.True(detail.IsSuccess);
        Assert.Equal("مالك الدار", detail.Value.OwnerArabicName);
        Assert.Equal("Facility Owner", detail.Value.OwnerEnglishName);
        Assert.Equal(3, detail.Value.ReviewHistory.Count);

        CareHomeAdminReviewHistory approved = detail.Value.ReviewHistory[0];
        Assert.Equal(actorUser.Id.Value, approved.ActorUserId);
        Assert.Equal("مراجع الدار", approved.ActorArabicName);
        Assert.Equal("Home Reviewer", approved.ActorEnglishName);
        Assert.Equal(["SupportAdmin"], approved.ActorCurrentRoles);

        CareHomeAdminReviewHistory suspended = detail.Value.ReviewHistory[1];
        Assert.Equal(anonUser.Id.Value, suspended.ActorUserId);
        Assert.Null(suspended.ActorArabicName);
        Assert.Null(suspended.ActorEnglishName);
        Assert.Null(suspended.ActorCurrentRoles);

        CareHomeAdminReviewHistory reactivated = detail.Value.ReviewHistory[2];
        Assert.Equal(missingReviewer.Value, reactivated.ActorUserId);
        Assert.Null(reactivated.ActorArabicName);
        Assert.Null(reactivated.ActorEnglishName);
        Assert.Null(reactivated.ActorCurrentRoles);

        string json = JsonSerializer.Serialize(detail.Value);
        Assert.DoesNotContain("+201000000101", json, StringComparison.Ordinal);
        Assert.DoesNotContain("+201000000102", json, StringComparison.Ordinal);
        Assert.DoesNotContain("owner@example.test", json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdminApplication_ActorCurrentRoles_FollowLiveAccountChange(bool sqlite)
    {
        await using var contexts = await DualContexts.CreateAsync(sqlite);
        IdentityDbContext identity = contexts.Identity;
        CareHomesDbContext db = contexts.CareHomes;

        User ownerUser = IdentityUser("+201000000111", "owner2@example.test", "مالك", "Owner");
        ownerUser.AddAccount(AccountType.CareHomeOwner);
        User actorUser = IdentityUser("+201000000112", "actor2@example.test", "مراجع", "Reviewer");
        actorUser.AddAccount(AccountType.SupportAdmin);
        identity.Users.AddRange(ownerUser, actorUser);
        await identity.SaveChangesAsync();

        CareHomeFacility facility = NewDraftFacility(ownerUser.Id);
        AddRequiredDocuments(facility, ownerUser.Id);
        facility.Submit(ownerUser.Id, facility.Version, Now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(actorUser.Id, facility.Version, document.Id, null, true, Now.AddMinutes(1));
        facility.Review(actorUser.Id, facility.Version, CareHomeReviewAction.Approved, null, Now.AddMinutes(2), DateOnly.FromDateTime(Now));
        db.Facilities.Add(facility);
        await db.SaveChangesAsync();

        var handler = new GetAdminCareHomeApplicationQueryHandler(db, new ForwardingSender(identity), CareHomeTestClockHost.Clock());
        var before = await handler.Handle(new GetAdminCareHomeApplicationQuery(facility.Id.Value), default);
        Assert.True(before.IsSuccess);
        Assert.Equal(["SupportAdmin"], before.Value.ReviewHistory.Single().ActorCurrentRoles);

        User liveActor = await identity.Users.Include(x => x.Accounts).SingleAsync(x => x.Id == actorUser.Id);
        liveActor.ChangeAdministrativeAccount(AccountType.SuperAdmin, Now.AddMinutes(5));
        await identity.SaveChangesAsync();

        var after = await handler.Handle(new GetAdminCareHomeApplicationQuery(facility.Id.Value), default);
        Assert.True(after.IsSuccess);
        Assert.Single(after.Value.ReviewHistory);
        Assert.Equal(["SuperAdmin"], after.Value.ReviewHistory.Single().ActorCurrentRoles);
    }

    private static User IdentityUser(string phone, string email, string arabic, string english) =>
        User.Create(FullName.Create(arabic), FullName.Create(english), Email.Create(email), PhoneNumber.Create(phone), null);

    private sealed class DualContexts : IAsyncDisposable
    {
        private readonly SqliteConnection? _identityConnection;
        private readonly SqliteConnection? _careHomesConnection;

        private DualContexts(IdentityDbContext identity, CareHomesDbContext careHomes, SqliteConnection? identityConnection, SqliteConnection? careHomesConnection)
        {
            Identity = identity;
            CareHomes = careHomes;
            _identityConnection = identityConnection;
            _careHomesConnection = careHomesConnection;
        }

        public IdentityDbContext Identity { get; }
        public CareHomesDbContext CareHomes { get; }

        public static async Task<DualContexts> CreateAsync(bool sqlite)
        {
            SqliteConnection? identityConnection = null;
            SqliteConnection? careHomesConnection = null;
            DbContextOptions<IdentityDbContext> identityOptions;
            DbContextOptions<CareHomesDbContext> careHomesOptions;
            if (sqlite)
            {
                identityConnection = new SqliteConnection("Data Source=:memory:");
                careHomesConnection = new SqliteConnection("Data Source=:memory:");
                await identityConnection.OpenAsync();
                await careHomesConnection.OpenAsync();
                identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseSqlite(identityConnection).Options;
                careHomesOptions = new DbContextOptionsBuilder<CareHomesDbContext>().UseSqlite(careHomesConnection).Options;
            }
            else
            {
                string name = Guid.NewGuid().ToString("N");
                identityOptions = new DbContextOptionsBuilder<IdentityDbContext>().UseInMemoryDatabase(name + "-identity").Options;
                careHomesOptions = new DbContextOptionsBuilder<CareHomesDbContext>().UseInMemoryDatabase(name + "-carehomes").Options;
            }
            var identity = new IdentityDbContext(identityOptions);
            var careHomes = new CareHomesDbContext(careHomesOptions);
            if (sqlite)
            {
                await identity.Database.EnsureCreatedAsync();
                await careHomes.Database.EnsureCreatedAsync();
            }
            return new DualContexts(identity, careHomes, identityConnection, careHomesConnection);
        }

        public async ValueTask DisposeAsync()
        {
            await Identity.DisposeAsync();
            await CareHomes.DisposeAsync();
            if (_identityConnection is not null) await _identityConnection.DisposeAsync();
            if (_careHomesConnection is not null) await _careHomesConnection.DisposeAsync();
        }
    }

    [Fact]
    public async Task FamilySubmit_DoesNotSerializeAdminEnrichmentFields()
    {
        using CareHomesDbContext db = CreateDb();
        CareHomeId facility = CareHomeId.New();
        CareHomeBooking booking = CreateBooking(facility, "مقيم", "Resident", 80);
        booking.MarkPaid(123, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        booking.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(3));
        booking.RecordCheckIn(UserId.New(), Now.AddMinutes(4));
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var submitted = await new CheckInDisputeHandlers(db).Handle(
            new SubmitCareHomeCheckInDisputeCommand(UserId.New(), booking.FamilyId, booking.Id, "Issue", Now.AddMinutes(5)), default);

        Assert.True(submitted.IsSuccess);
        string json = JsonSerializer.Serialize(submitted.Value, WebOptions);
        foreach (string field in new[] { "residentArabicName", "residentEnglishName", "originalBookingTotalAmount", "originalBookingCurrency" })
            Assert.DoesNotContain(field, json, StringComparison.Ordinal);
        Assert.Contains("familyReason", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AdminResolve_ResponseShapeRemainsUnchanged()
    {
        using CareHomesDbContext db = CreateDb();
        CareHomeId facility = CareHomeId.New();
        CareHomeBooking booking = CreateBooking(facility, "مقيم", "Resident", 80);
        db.Bookings.Add(booking);
        var dispute = CareHomeCheckInDispute.Open(booking.Id, facility, null, UserId.New(), Now, "Issue");
        db.CheckInDisputes.Add(dispute);
        await db.SaveChangesAsync();

        var resolved = await new CheckInDisputeHandlers(db).Handle(
            new ResolveCareHomeCheckInDisputeCommand(UserId.New(), dispute.Id, Now.AddMinutes(1), "Evidence", "Reason", Now.AddMinutes(2)), default);

        Assert.True(resolved.IsSuccess);
        string json = JsonSerializer.Serialize(resolved.Value, WebOptions);
        foreach (string field in new[] { "residentArabicName", "residentEnglishName", "originalBookingTotalAmount", "originalBookingCurrency" })
            Assert.DoesNotContain(field, json, StringComparison.Ordinal);
        foreach (string field in new[] { "bookingId", "facilityId", "status", "effectiveCheckInOnUtc", "evidence", "reason", "resolvedBy" })
            Assert.Contains(field, json, StringComparison.Ordinal);
    }

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

    private static CareHomeProfileMedia Media(Guid revisionId, CareHomeProfileMediaKind kind, int position, string key) =>
        CareHomeProfileMedia.Create(revisionId, kind, position, key, "image/jpeg", 100, Now);

    private static CareHomeBooking CreateBooking(CareHomeId facility, string arabic, string english, int age) =>
        CareHomeBooking.Create(facility, UserId.New(), FamilyId.New(), ElderlyId.New(), Guid.NewGuid(),
            new DateOnly(2026, 10, 20), 1000m, 100m, 50m, 4, arabic, english, age,
            null, "Contact", null, null, null, Now);

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static CareHomeFacility SeedFacility(CareHomesDbContext db, UserId owner)
    {
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, Now);
        db.Facilities.Add(facility);
        return facility;
    }

    [Fact]
    public async Task OwnerDisputes_EmptyOwner_ReturnsPagedEmpty()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, null, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(25, result.Value.PageSize);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task OwnerDisputes_FacilityWithoutDisputes_ReturnsPagedEmpty()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        db.Facilities.Add(CareHomeFacility.CreateDraft(owner, Now));
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 2, 10, null, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Page);
        Assert.Equal(10, result.Value.PageSize);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task OwnerDisputes_OwnFacility_DisputesAreVisibleAndPaged()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        CareHomeFacility foreign = SeedFacility(db, UserId.New());

        var open = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now, "Family issue");
        var resolved = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now.AddMinutes(1));
        resolved.Resolve(UserId.New(), Now.AddMinutes(2), "Signed log", "Reconciled", Now.AddMinutes(3));
        var foreignDispute = CareHomeCheckInDispute.Open(Guid.NewGuid(), foreign.Id, null, UserId.New(), Now.AddMinutes(5));
        db.CheckInDisputes.AddRange(open, resolved, foreignDispute);
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, null, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.TotalCount);
        Assert.Equal(2, result.Value.Items.Count);
        Guid[] ids = result.Value.Items.Select(x => x.Id).ToArray();
        Assert.Equal(new[] { resolved.Id, open.Id }, ids);
        Assert.DoesNotContain(result.Value.Items, x => x.Id == foreignDispute.Id);

        var filtered = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, "open", Now), default);
        Assert.Equal(1, filtered.Value.TotalCount);
        Assert.Equal(CareHomeCheckInDisputeStatus.Open, Assert.Single(filtered.Value.Items).Status);
    }

    [Fact]
    public async Task OwnerDisputes_ForeignOwner_IsolatedFromOtherFacilityCases()
    {
        using CareHomesDbContext db = CreateDb();
        UserId ownerA = UserId.New();
        UserId ownerB = UserId.New();
        CareHomeFacility facilityA = SeedFacility(db, ownerA);
        CareHomeFacility facilityB = SeedFacility(db, ownerB);
        db.CheckInDisputes.Add(CareHomeCheckInDispute.Open(Guid.NewGuid(), facilityB.Id, null, UserId.New(), Now));
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(ownerA, 1, 25, null, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task OwnerDisputes_StatusFilter_IsCaseInsensitiveAndCountsAfterFiltering()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        var resolved = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now);
        resolved.Resolve(UserId.New(), Now.AddMinutes(1), "Signed log", "Reconciled", Now.AddMinutes(2));
        db.CheckInDisputes.AddRange(
            CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now),
            CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now.AddMinutes(1)),
            resolved);
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 1, "RESOLVED", Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        OwnerCareHomeCheckInDispute single = Assert.Single(result.Value.Items);
        Assert.Equal(CareHomeCheckInDisputeStatus.Resolved, single.Status);
        Assert.NotNull(single.ResolvedOnUtc);
        Assert.NotNull(single.EffectiveCheckInOnUtc);
    }

    [Theory]
    [InlineData("Open", true)]
    [InlineData("open", true)]
    [InlineData("RESOLVED", true)]
    [InlineData("0", false)]
    [InlineData("2", false)]
    [InlineData("99", false)]
    [InlineData("Closed", false)]
    public async Task OwnerDisputes_StatusValidator_AcceptsOnlyCaseInsensitiveNames(string? status, bool valid)
    {
        var result = await new OwnerCareHomeCheckInDisputesQueryValidator()
            .ValidateAsync(new ListOwnerCareHomeCheckInDisputesQuery(UserId.New(), 1, 25, status, Now));

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task OwnerDisputes_Pagination_OrderAndPostFilterTotalMatch()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        Guid[] orderedIds = new Guid[6];
        for (int i = 0; i < 6; i++)
        {
            var dispute = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now.AddMinutes(i));
            if (i is 2 or 4)
                dispute.Resolve(UserId.New(), Now.AddMinutes(i + 1), "Signed log", "Reconciled", Now.AddMinutes(i + 2));
            db.CheckInDisputes.Add(dispute);
            orderedIds[i] = dispute.Id;
        }
        await db.SaveChangesAsync();
        Array.Reverse(orderedIds);

        var page1 = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 4, null, Now), default);
        var page2 = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 2, 4, null, Now), default);

        Assert.Equal(6, page1.Value.TotalCount);
        Assert.Equal(orderedIds.Take(4).ToArray(), page1.Value.Items.Select(x => x.Id).ToArray());
        Assert.Equal(6, page2.Value.TotalCount);
        Assert.Equal(orderedIds.Skip(4).ToArray(), page2.Value.Items.Select(x => x.Id).ToArray());
    }

    [Fact]
    public async Task OwnerDisputes_EqualOpenedTime_OrdersByCaseIdDescending()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        DateTime opened = Now.AddHours(-1);
        var first = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), opened);
        var second = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), opened);
        var third = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), opened);
        db.CheckInDisputes.AddRange(first, second, third);
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, null, Now), default);

        Assert.Equal(new[] { first.Id, second.Id, third.Id }.OrderByDescending(id => id), result.Value.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task OwnerDisputes_BookingUnavailable_EnrichmentFieldsAreNull()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        db.CheckInDisputes.Add(CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now));
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, null, Now), default);

        Assert.True(result.IsSuccess);
        OwnerCareHomeCheckInDispute item = Assert.Single(result.Value.Items);
        Assert.Null(item.ResidentArabicName);
        Assert.Null(item.ResidentEnglishName);
        Assert.Null(item.OriginalBookingTotalAmount);
        Assert.Null(item.OriginalBookingCurrency);
    }

    [Fact]
    public async Task OwnerDisputes_OpenCase_NullsCheckoutAndOutcomeTimestamps()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        var open = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now, "Family issue");
        db.CheckInDisputes.Add(open);
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, null, Now), default);

        OwnerCareHomeCheckInDispute item = Assert.Single(result.Value.Items);
        Assert.Equal(CareHomeCheckInDisputeStatus.Open, item.Status);
        Assert.Null(item.CheckoutOnUtc);
        Assert.Equal("Family issue", item.FamilyReason);
        Assert.Null(item.EffectiveCheckInOnUtc);
        Assert.Null(item.ResolvedOnUtc);
    }

    [Fact]
    public async Task OwnerDisputes_OutcomeResolution_DoesNotRewriteBookingTimestamps()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        CareHomeBooking booking = CreateBooking(facility.Id, "مقيم", "Resident", 80);
        booking.MarkPaid(123, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        booking.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(3));
        booking.RecordCheckIn(UserId.New(), Now.AddMinutes(4));
        booking.RecordCheckOut(UserId.New(), Now.AddMinutes(5));
        db.Bookings.Add(booking);
        var dispute = CareHomeCheckInDispute.Open(booking.Id, facility.Id, Now.AddMinutes(5), UserId.New(), Now.AddMinutes(6));
        dispute.Resolve(UserId.New(), Now.AddMinutes(7), "Signed log", "Reconciled", Now.AddMinutes(8));
        db.CheckInDisputes.Add(dispute);
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, "Resolved", Now), default);

        OwnerCareHomeCheckInDispute item = Assert.Single(result.Value.Items);
        Assert.Equal(Now.AddMinutes(7), item.EffectiveCheckInOnUtc);
        Assert.Equal(Now.AddMinutes(5), item.CheckoutOnUtc);
        Assert.NotNull(item.OriginalBookingTotalAmount);
        Assert.Equal("EGP", item.OriginalBookingCurrency);

        CareHomeBooking persisted = await db.Bookings.AsNoTracking().SingleAsync(x => x.Id == booking.Id);
        Assert.Equal(Now.AddMinutes(4), persisted.ActualCheckInOnUtc);
        Assert.Equal(Now.AddMinutes(5), persisted.ActualCheckOutOnUtc);
    }

    [Fact]
    public async Task OwnerDisputes_ResponseJson_ExcludesAdminResolutionFields()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        CareHomeFacility facility = SeedFacility(db, owner);
        var dispute = CareHomeCheckInDispute.Open(Guid.NewGuid(), facility.Id, null, UserId.New(), Now);
        dispute.Resolve(UserId.New(), Now.AddMinutes(1), "Signed log", "Reconciled", Now.AddMinutes(2));
        db.CheckInDisputes.Add(dispute);
        await db.SaveChangesAsync();

        var result = await new OwnerCareHomeCheckInDisputesHandler(db)
            .Handle(new ListOwnerCareHomeCheckInDisputesQuery(owner, 1, 25, null, Now), default);

        Assert.True(result.IsSuccess);
        string json = JsonSerializer.Serialize(result.Value, WebOptions);
        foreach (string field in new[] { "evidence", "reason", "resolvedBy", "openedBy" })
            Assert.DoesNotContain(field, json, StringComparison.Ordinal);
        foreach (string field in new[] { "id", "bookingId", "facilityId", "status", "openedOnUtc", "familyReason", "effectiveCheckInOnUtc", "resolvedOnUtc" })
            Assert.Contains(field, json, StringComparison.Ordinal);
    }
}
