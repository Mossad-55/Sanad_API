using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.API.CaregiversIntegration;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Infrastructure.Persistence;
using Xunit;

namespace Sanad.UnitTests.Families;

/// <summary>
/// W1 admin booking discovery: the optional <c>status</c> filter contract and the display-name
/// projection on <c>GET /api/v1/admin/bookings</c>.
/// <para>
/// Status contract (Owner refinement): an absent status preserves the existing finance behavior
/// (closed-list default); a defined status with <c>finance=All</c> returns exactly that status;
/// for the other finance values the status must belong to the finance predicate
/// (Cancelled = 6/7/8, FailedRefund = 6/7/8/10 plus the paid/unrefunded condition,
/// Refunded = 9) and every other explicit combination is a coded 400. Undefined numeric enum
/// values are rejected by the handler because MVC enum binding accepts them.
/// </para>
/// <para>Executed in the combined focused W1–W4 suite and full Release suite.</para>
/// </summary>
public sealed class AdminBookingStatusFilterTests
{
    private static readonly DateTime CreatedOnUtc = new(2026, 10, 20, 8, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime AfterDeadlineUtc = new(2026, 10, 25, 11, 30, 0, DateTimeKind.Utc);
    private static readonly DateTime ScheduledStartUtc = new(2026, 10, 25, 12, 0, 0, DateTimeKind.Utc);

    private static readonly BookingStatus[] ClosedDefaultStatuses =
    [
        BookingStatus.CancelledByFamily,
        BookingStatus.DeclinedByCaregiver,
        BookingStatus.CancelledByCaregiver,
        BookingStatus.Refunded,
        BookingStatus.Expired
    ];

    [Fact]
    public async Task AbsentStatus_PreservesTheClosedListDefault_ForEveryFinanceValue()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        SeedEveryStatus(databaseName, family, elderly);
        ListAdminBookingsQueryHandler handler = CreateHandler(databaseName, new EmptyCaregiverDirectory());

        var all = await handler.Handle(new ListAdminBookingsQuery(1, 50), CancellationToken.None);
        Assert.True(all.IsSuccess);
        Assert.Equal(6, all.Value.TotalCount);
        Assert.All(all.Value.Items, item => Assert.Contains(item.Status, ClosedDefaultStatuses));

        var cancelled = await handler.Handle(
            new ListAdminBookingsQuery(1, 50, AdminBookingFinanceFilter.Cancelled),
            CancellationToken.None);
        Assert.Equal(4, cancelled.Value.TotalCount);
        Assert.All(cancelled.Value.Items, item => Assert.True(
            item.Status is BookingStatus.CancelledByFamily
                or BookingStatus.DeclinedByCaregiver
                or BookingStatus.CancelledByCaregiver));

        // finance=2 keeps the paid/unrefunded predicate: the unpaid family-cancel row is excluded.
        var failedRefund = await handler.Handle(
            new ListAdminBookingsQuery(1, 50, AdminBookingFinanceFilter.FailedRefund),
            CancellationToken.None);
        Assert.Equal(4, failedRefund.Value.TotalCount);
        Assert.All(failedRefund.Value.Items, item => Assert.True(
            item.PaidOnUtc is not null && item.RefundedOnUtc is null));

        var refunded = await handler.Handle(
            new ListAdminBookingsQuery(1, 50, AdminBookingFinanceFilter.Refunded),
            CancellationToken.None);
        Assert.Equal(1, refunded.Value.TotalCount);
        Assert.Equal(BookingStatus.Refunded, refunded.Value.Items.Single().Status);
    }

    [Theory]
    [InlineData((int)BookingStatus.PendingPayment, 1)]
    [InlineData((int)BookingStatus.PendingCaregiverApproval, 1)]
    [InlineData((int)BookingStatus.Confirmed, 1)]
    [InlineData((int)BookingStatus.InProgress, 1)]
    [InlineData((int)BookingStatus.Completed, 1)]
    [InlineData((int)BookingStatus.CancelledByFamily, 2)]
    [InlineData((int)BookingStatus.DeclinedByCaregiver, 1)]
    [InlineData((int)BookingStatus.CancelledByCaregiver, 1)]
    [InlineData((int)BookingStatus.Refunded, 1)]
    [InlineData((int)BookingStatus.Expired, 1)]
    public async Task DefinedStatus_WithAllFinance_ReturnsExactlyThatStatus(
        int statusValue,
        int seededCount)
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        SeedEveryStatus(databaseName, family, elderly);
        var status = (BookingStatus)statusValue;
        ListAdminBookingsQueryHandler handler = CreateHandler(databaseName, new EmptyCaregiverDirectory());

        var result = await handler.Handle(
            new ListAdminBookingsQuery(1, 50, AdminBookingFinanceFilter.All, status),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(seededCount, result.Value.TotalCount);
        Assert.All(result.Value.Items, item => Assert.Equal(status, item.Status));
    }

    [Fact]
    public async Task ConfirmedBookings_AreDiscoverable_ByExplicitStatus_Alone()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        SeedBooking(databaseName, family, elderly, BookingStatus.Confirmed);

        var items = await QueryItemsAsync(
            databaseName, AdminBookingFinanceFilter.All, BookingStatus.Confirmed);

        AdminBookingListItemResponse item = items.Single();
        Assert.Equal(BookingStatus.Confirmed, item.Status);
        Assert.Equal(family.Id.Value, item.FamilyId);
        Assert.Equal("Admin Booking Family", item.FamilyName);
    }

    [Theory]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.CancelledByFamily)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.DeclinedByCaregiver)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.CancelledByCaregiver)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.CancelledByFamily)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.DeclinedByCaregiver)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.CancelledByCaregiver)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.Expired)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.Refunded)]
    public async Task StatusInsideTheFinancePredicate_IsAllowed_AndAppliesThePredicate(
        int financeValue,
        int statusValue)
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        SeedEveryStatus(databaseName, family, elderly);
        ListAdminBookingsQueryHandler handler = CreateHandler(databaseName, new EmptyCaregiverDirectory());

        var result = await handler.Handle(
            new ListAdminBookingsQuery(1, 50, (AdminBookingFinanceFilter)financeValue, (BookingStatus)statusValue),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.All(result.Value.Items, item => Assert.Equal((BookingStatus)statusValue, item.Status));
    }

    [Fact]
    public async Task FailedRefund_WithAnExplicitStatus_KeepsThePaidUnrefundedCondition()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily, paid: false);

        var items = await QueryItemsAsync(
            databaseName, AdminBookingFinanceFilter.FailedRefund, BookingStatus.CancelledByFamily);

        // The unpaid cancellation is a member of the finance predicate, but the paid/unrefunded
        // condition still excludes it: an allowed combination yields its honest empty page.
        Assert.Empty(items);
    }

    [Theory]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.PendingPayment)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.PendingCaregiverApproval)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.Confirmed)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.InProgress)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.Completed)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.Refunded)]
    [InlineData((int)AdminBookingFinanceFilter.Cancelled, (int)BookingStatus.Expired)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.PendingPayment)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.PendingCaregiverApproval)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.Confirmed)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.InProgress)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.Completed)]
    [InlineData((int)AdminBookingFinanceFilter.FailedRefund, (int)BookingStatus.Refunded)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.PendingPayment)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.PendingCaregiverApproval)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.Confirmed)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.InProgress)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.Completed)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.CancelledByFamily)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.DeclinedByCaregiver)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.CancelledByCaregiver)]
    [InlineData((int)AdminBookingFinanceFilter.Refunded, (int)BookingStatus.Expired)]
    public async Task StatusOutsideTheFinancePredicate_IsACoded400(
        int financeValue,
        int statusValue)
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        SeedEveryStatus(databaseName, family, elderly);

        var result = await HandleAsync(
            databaseName,
            new ListAdminBookingsQuery(1, 10, (AdminBookingFinanceFilter)financeValue, (BookingStatus)statusValue));

        Assert.False(result.IsSuccess);
        Assert.Equal(ListAdminBookingsQueryHandler.InvalidStatusFilterErrorCode, result.Error.Code);
    }

    [Fact]
    public async Task UndefinedFinanceEnum_IsRejected_EvenWithoutStatus()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);

        var result = await HandleAsync(
            databaseName,
            new ListAdminBookingsQuery(1, 10, (AdminBookingFinanceFilter)99, null));

        Assert.False(result.IsSuccess);
        Assert.Equal(ListAdminBookingsQueryHandler.InvalidStatusFilterErrorCode, result.Error.Code);
    }

    [Fact]
    public async Task UndefinedStatusEnum_IsRejected_EvenWithDefaultFinance()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);

        var result = await HandleAsync(
            databaseName,
            new ListAdminBookingsQuery(1, 10, AdminBookingFinanceFilter.All, (BookingStatus)99));

        Assert.False(result.IsSuccess);
        Assert.Equal(ListAdminBookingsQueryHandler.InvalidStatusFilterErrorCode, result.Error.Code);
    }

    [Fact]
    public async Task Paging_ClampsInvalidInputs_AsBefore()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily);

        Result<PagedAdminBookings> clamped = await HandleAsync(databaseName, new ListAdminBookingsQuery(0, 0));
        Assert.True(clamped.IsSuccess);
        Assert.Equal(1, clamped.Value.Page);
        Assert.Equal(10, clamped.Value.PageSize);

        Result<PagedAdminBookings> oversized = await HandleAsync(databaseName, new ListAdminBookingsQuery(1, 500));
        Assert.True(oversized.IsSuccess);
        Assert.Equal(10, oversized.Value.PageSize);
    }

    [Fact]
    public async Task TiedCancellationTimestamps_PartitionPages_Deterministically()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);
        Booking first = SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily);
        Booking second = SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily);

        // The fixture stamps both cancellations with fixed constants, so the sort key ties and
        // only the unique id tiebreaker may order the two rows.
        Assert.Equal(first.CancelledOnUtc, second.CancelledOnUtc);

        Result<PagedAdminBookings> pageOne = await HandleAsync(databaseName, new ListAdminBookingsQuery(0, 1));
        Result<PagedAdminBookings> pageTwo = await HandleAsync(databaseName, new ListAdminBookingsQuery(2, 1));
        Result<PagedAdminBookings> pageOneAgain = await HandleAsync(databaseName, new ListAdminBookingsQuery(0, 1));

        Assert.Equal(2, pageOne.Value.TotalCount);
        Assert.Single(pageOne.Value.Items);
        Assert.NotEqual(pageOne.Value.Items[0].Id, pageTwo.Value.Items[0].Id);
        Assert.Equal(
            pageOne.Value.Items.Select(item => item.Id),
            pageOneAgain.Value.Items.Select(item => item.Id));
    }

    [Fact]
    public async Task Names_BatchedPerPage_WithExplicitNulls()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);

        CaregiverId namedMedical = CaregiverId.New();
        CaregiverId namedCompanion = CaregiverId.New();
        CaregiverId withheldCaregiver = CaregiverId.New();
        CaregiverId absentCaregiver = CaregiverId.New();

        // namedMedical/namedCompanion publish names (medical AND companion caregiver types);
        // withheldCaregiver stands for an anonymized identity; absentCaregiver has no directory
        // entry at all. Both must read as explicit nulls in the projection.
        var directory = new DictionaryCaregiverDirectory(new Dictionary<Guid, (string?, string?)>
        {
            [namedMedical.Value] = ("دكتور تجريبي", "Doctor Test"),
            [namedCompanion.Value] = ("رفيق تجريبي", "Companion Test"),
        });

        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily, caregiverId: namedMedical);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily, caregiverId: namedCompanion);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily, caregiverId: withheldCaregiver);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily, caregiverId: absentCaregiver);

        ListAdminBookingsQueryHandler handler = new(CreateContext(databaseName), directory);

        Result<PagedAdminBookings> result = await handler.Handle(
            new ListAdminBookingsQuery(1, 50), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.TotalCount);
        Assert.Single(directory.Batches);
        Assert.Equal(4, directory.Batches[0].Count);

        Dictionary<Guid, AdminBookingListItemResponse> itemsByCaregiverId =
            result.Value.Items.ToDictionary(item => item.CaregiverId);

        Assert.Equal("دكتور تجريبي", itemsByCaregiverId[namedMedical.Value].CaregiverArabicFullName);
        Assert.Equal("Doctor Test", itemsByCaregiverId[namedMedical.Value].CaregiverEnglishFullName);
        Assert.Equal("رفيق تجريبي", itemsByCaregiverId[namedCompanion.Value].CaregiverArabicFullName);
        Assert.Equal("Companion Test", itemsByCaregiverId[namedCompanion.Value].CaregiverEnglishFullName);
        Assert.Null(itemsByCaregiverId[withheldCaregiver.Value].CaregiverArabicFullName);
        Assert.Null(itemsByCaregiverId[withheldCaregiver.Value].CaregiverEnglishFullName);
        Assert.Null(itemsByCaregiverId[absentCaregiver.Value].CaregiverArabicFullName);
        Assert.Null(itemsByCaregiverId[absentCaregiver.Value].CaregiverEnglishFullName);
    }

    [Fact]
    public async Task DeletedFamilySentinel_ProjectsToNull_NeverAsADisplayName()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            Family stored = context.Families.Single(f => f.Id == family.Id);
            stored.MarkDeleted("account closure", "done");
            context.SaveChanges();
        }

        SeedBooking(databaseName, family, elderly, BookingStatus.Confirmed);

        var items = await QueryItemsAsync(
            databaseName, AdminBookingFinanceFilter.All, BookingStatus.Confirmed);

        AdminBookingListItemResponse item = items.Single();
        Assert.Null(item.FamilyName);
    }

    [Fact]
    public async Task MissingFamilyRow_ProjectsToNull()
    {
        string databaseName = NewDatabaseName();
        (Family family, Elderly elderly) = SeedFamily(databaseName);

        using (FamiliesDbContext context = CreateContext(databaseName))
        {
            context.Families.Remove(context.Families.Single(f => f.Id == family.Id));
            context.SaveChanges();
        }

        SeedBooking(databaseName, family, elderly, BookingStatus.Confirmed);

        var items = await QueryItemsAsync(
            databaseName, AdminBookingFinanceFilter.All, BookingStatus.Confirmed);

        AdminBookingListItemResponse item = items.Single();
        Assert.Null(item.FamilyName);
    }

    [Fact]
    public async Task RealAdapter_ResolvesMedicalAndCompanionNames_ThroughThePublishedIdentityQuery()
    {
        string databaseName = NewDatabaseName();
        using IdentityDbContext identity = CreateIdentityContext(databaseName);
        using CaregiversDbContext caregivers = CreateCaregiversContext(databaseName);

        User medicalUser = IdentityUser("+201000000101", "medical@example.test", "دكتور", "Medical");
        User companionUser = IdentityUser("+201000000102", "companion@example.test", "رفيق", "Companion");
        User anonymizedUser = IdentityUser("+201000000103", "anon@example.test", "مجهول", "Anon");
        anonymizedUser.AnonymizeAndDeactivate(CreatedOnUtc);
        identity.Users.AddRange(medicalUser, companionUser, anonymizedUser);
        identity.SaveChanges();

        Caregiver medicalCaregiver = Caregiver.Create(medicalUser.Id, CaregiverType.Medical);
        Caregiver companionCaregiver = Caregiver.Create(companionUser.Id, CaregiverType.Companion);
        Caregiver anonymizedCaregiver = Caregiver.Create(anonymizedUser.Id, CaregiverType.Medical);
        caregivers.Caregivers.AddRange(medicalCaregiver, companionCaregiver, anonymizedCaregiver);
        caregivers.SaveChanges();

        var adapter = new MedicalCaregiverGateway(caregivers, new ForwardingSender(identity));

        Guid unknownCaregiverId = Guid.NewGuid();
        IReadOnlyDictionary<Guid, CaregiverDisplayName> names = await adapter.GetCaregiverDisplayNamesAsync(
            [medicalCaregiver.Id.Value, companionCaregiver.Id.Value, anonymizedCaregiver.Id.Value, unknownCaregiverId]);

        // medical, companion and anonymized caregivers each resolve through two batched reads;
        // the unknown id has no caregiver row and is simply absent from the directory.
        Assert.Equal(3, names.Count);
        Assert.False(names.ContainsKey(unknownCaregiverId));

        Assert.Equal("دكتور", names[medicalCaregiver.Id.Value].ArabicFullName);
        Assert.Equal("Medical", names[medicalCaregiver.Id.Value].EnglishFullName);
        Assert.Equal("رفيق", names[companionCaregiver.Id.Value].ArabicFullName);
        Assert.Equal("Companion", names[companionCaregiver.Id.Value].EnglishFullName);

        // The anonymized identity is withheld by the published display-info query — the adapter
        // must answer nulls, never revive hidden personal data.
        Assert.Null(names[anonymizedCaregiver.Id.Value].ArabicFullName);
        Assert.Null(names[anonymizedCaregiver.Id.Value].EnglishFullName);
    }

    // ---------------------------------------------------------------------------------------------
    // Fixtures.
    // ---------------------------------------------------------------------------------------------

    private static string NewDatabaseName() => $"sanad-admin-booking-filters-{Guid.NewGuid():N}";

    private static FamiliesDbContext CreateContext(string databaseName) =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static IdentityDbContext CreateIdentityContext(string databaseName) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static CaregiversDbContext CreateCaregiversContext(string databaseName) =>
        new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(databaseName)
            .Options);

    private static ListAdminBookingsQueryHandler CreateHandler(
        string databaseName,
        IMedicalCaregiverGateway directory) =>
        new(CreateContext(databaseName), directory);

    private static async Task<Result<PagedAdminBookings>> HandleAsync(
        string databaseName,
        ListAdminBookingsQuery query) =>
        await CreateHandler(databaseName, new EmptyCaregiverDirectory())
            .Handle(query, CancellationToken.None);

    private static async Task<IReadOnlyList<AdminBookingListItemResponse>> QueryItemsAsync(
        string databaseName,
        AdminBookingFinanceFilter finance,
        BookingStatus? status)
    {
        Result<PagedAdminBookings> result = await HandleAsync(
            databaseName, new ListAdminBookingsQuery(1, 50, finance, status));

        Assert.True(result.IsSuccess);
        return result.Value.Items;
    }

    private static (Family Family, Elderly Elderly) SeedFamily(string databaseName)
    {
        using FamiliesDbContext context = CreateContext(databaseName);

        var family = Family.Create(UserId.New(), "Admin Booking Family");

        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(CreatedOnUtc.AddYears(-70)),
            DateOnly.FromDateTime(CreatedOnUtc));

        context.Families.Add(family);
        context.Elderlies.Add(elderly);
        context.SaveChanges();

        return (family, elderly);
    }

    /// <summary>
    /// Seeds one booking driven through public domain transitions to the requested status, then
    /// persists it. Timestamps advance in fixed one-minute steps from <see cref="CreatedOnUtc"/>,
    /// so two bookings with the same lifecycle land on identical ordering keys (see the tie test).
    /// </summary>
    private static Booking SeedBooking(
        string databaseName,
        Family family,
        Elderly elderly,
        BookingStatus target,
        bool paid = true,
        CaregiverId? caregiverId = null)
    {
        Booking booking = Booking.Create(
            family.Id,
            family.OwnerUserId,
            elderly.Id,
            caregiverId ?? CaregiverId.New(),
            BookingCaregiverType.Companion,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc),
            TimeOnly.FromDateTime(ScheduledStartUtc).AddHours(2),
            "123 Nile St, Cairo",
            null,
            BookingPriceSnapshot.Calculate(400m, 0m, 0m, 1),
            acceptanceDeadlineUtc: ScheduledStartUtc.AddHours(-2),
            currentDate: DateOnly.FromDateTime(CreatedOnUtc),
            createdOnUtc: CreatedOnUtc,
            refundPolicyVersion: BookingRefundPolicyVersion.ForNewBooking(
                scheduledStartPolicyActivated: true));

        DateTime stamp = CreatedOnUtc;

        if (paid)
        {
            booking.RecordPaymentIntent("order", PaymentMethod.Card, stamp);
            booking.MarkAsPaid("order", $"txn-{target}", stamp);
        }

        switch (target)
        {
            case BookingStatus.PendingPayment:
            case BookingStatus.PendingCaregiverApproval:
                break;
            case BookingStatus.Confirmed:
                booking.AcceptByCaregiver(stamp.AddMinutes(1));
                break;
            case BookingStatus.InProgress:
                booking.AcceptByCaregiver(stamp.AddMinutes(1));
                booking.StartVisit(stamp.AddMinutes(2));
                break;
            case BookingStatus.Completed:
                booking.AcceptByCaregiver(stamp.AddMinutes(1));
                booking.StartVisit(stamp.AddMinutes(2));
                booking.CompleteVisit(null, stamp.AddMinutes(3));
                break;
            case BookingStatus.CancelledByFamily:
                booking.CancelByFamily("family emergency", stamp.AddMinutes(1));
                break;
            case BookingStatus.DeclinedByCaregiver:
                booking.DeclineByCaregiver("unavailable", stamp.AddMinutes(1));
                break;
            case BookingStatus.CancelledByCaregiver:
                booking.AcceptByCaregiver(stamp.AddMinutes(1));
                booking.CancelByCaregiver("caregiver emergency", stamp.AddMinutes(2));
                break;
            case BookingStatus.Refunded:
                booking.CancelByFamily("family emergency", stamp.AddMinutes(1));
                booking.MarkRefunded("refund-txn", stamp.AddMinutes(2));
                break;
            case BookingStatus.Expired:
                booking.Expire(AfterDeadlineUtc);
                break;
        }

        using FamiliesDbContext context = CreateContext(databaseName);
        context.Bookings.Add(booking);
        context.SaveChanges();

        return booking;
    }

    /// <summary>
    /// Eleven rows covering every defined status plus the unpaid family cancellation that the
    /// FailedRefund predicate must keep excluding: 1×PendingPayment, 1×PendingCaregiverApproval,
    /// 1×Confirmed, 1×InProgress, 1×Completed, 2×CancelledByFamily (paid and unpaid),
    /// 1×DeclinedByCaregiver, 1×CancelledByCaregiver, 1×Refunded, 1×Expired.
    /// </summary>
    private static void SeedEveryStatus(string databaseName, Family family, Elderly elderly)
    {
        SeedBooking(databaseName, family, elderly, BookingStatus.PendingPayment, paid: false);
        SeedBooking(databaseName, family, elderly, BookingStatus.PendingCaregiverApproval);
        SeedBooking(databaseName, family, elderly, BookingStatus.Confirmed);
        SeedBooking(databaseName, family, elderly, BookingStatus.InProgress);
        SeedBooking(databaseName, family, elderly, BookingStatus.Completed);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByFamily, paid: false);
        SeedBooking(databaseName, family, elderly, BookingStatus.DeclinedByCaregiver);
        SeedBooking(databaseName, family, elderly, BookingStatus.CancelledByCaregiver);
        SeedBooking(databaseName, family, elderly, BookingStatus.Refunded);
        SeedBooking(databaseName, family, elderly, BookingStatus.Expired);
    }

    private static User IdentityUser(string phone, string email, string arabic, string english) =>
        User.Create(
            FullName.Create(arabic),
            FullName.Create(english),
            Email.Create(email),
            PhoneNumber.Create(phone),
            null);

    /// <summary>
    /// Omits <c>GetCaregiverDisplayNamesAsync</c> on purpose: the default interface method is the
    /// seam that keeps pre-existing test doubles compiling, and this fake also proves that default
    /// ("no published names") reads as explicit nulls in the projection.
    /// </summary>
    private sealed class EmptyCaregiverDirectory : IMedicalCaregiverGateway
    {
        public Task<bool> IsActiveMedicalCaregiverAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MedicalCaregiverRecipientPage(
                new List<MedicalCaregiverRecipient>(),
                0));
    }

    private sealed class DictionaryCaregiverDirectory(
        Dictionary<Guid, (string? Arabic, string? English)> namesById) : IMedicalCaregiverGateway
    {
        public List<IReadOnlyCollection<Guid>> Batches { get; } = [];

        public Task<bool> IsActiveMedicalCaregiverAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task<MedicalCaregiverRecipientPage> SearchActiveMedicalCaregiversAsync(
            string? search,
            int page,
            int pageSize,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MedicalCaregiverRecipientPage(
                new List<MedicalCaregiverRecipient>(),
                0));

        public Task<IReadOnlyDictionary<Guid, CaregiverDisplayName>> GetCaregiverDisplayNamesAsync(
            IReadOnlyCollection<Guid> caregiverIds,
            CancellationToken cancellationToken = default)
        {
            Batches.Add(caregiverIds);

            return Task.FromResult<IReadOnlyDictionary<Guid, CaregiverDisplayName>>(
                caregiverIds.ToDictionary(
                    id => id,
                    id => namesById.TryGetValue(id, out (string? Arabic, string? English) names)
                        ? new CaregiverDisplayName(id, names.Arabic, names.English)
                        : new CaregiverDisplayName(id, null, null)));
        }
    }

    /// <summary>
    /// Forwards only the published Identity display-info query to its real handler, so the
    /// production adapter is exercised against the actual anonymization semantics.
    /// </summary>
    private sealed class ForwardingSender(IdentityDbContext identity) : ISender
    {
        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            if (request is GetUserDisplayInfoByIdsQuery query)
            {
                var result = await new GetUserDisplayInfoByIdsQueryHandler(identity)
                    .Handle(query, cancellationToken);

                return (TResponse)(object)result;
            }

            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
