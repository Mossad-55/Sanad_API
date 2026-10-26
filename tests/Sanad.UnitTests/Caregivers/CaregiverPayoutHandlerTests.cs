using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Application.PayoutAccounts;
using Sanad.Modules.Caregivers.Application.Payouts;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Application;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverPayoutHandlerTests
{
    [Fact]
    public async Task Record_ShouldCreatePaidPayoutWithSnapshots()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(dbContext, caregiver.Id);

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var result =
            await handler.Handle(
                new RecordCaregiverPayoutCommand(
                    booking.Id.Value,
                    UserId.New(),
                    "BANK-2026-000123",
                    "Transfer advice 000123",
                    "October weekly payout",
                    Utc(10)),
                default);

        Assert.True(result.IsSuccess);
        Assert.Equal(booking.Id.Value, result.Value.BookingId);
        Assert.Equal(caregiver.Id.Value, result.Value.CaregiverId);
        Assert.Equal(150m, result.Value.GrossAmount);
        Assert.Equal(22.50m, result.Value.PlatformFeeAmount);
        Assert.Equal(result.Value.GrossAmount, result.Value.NetAmount);
        Assert.Equal("EGP", result.Value.Currency);
        Assert.Equal("Paid", result.Value.Status);
        Assert.Equal("NBE", result.Value.BankCode);
        Assert.Equal("****0002", result.Value.MaskedIban);
        Assert.Equal(2, result.Value.PolicyVersion);
        Assert.Equal(Utc(10), result.Value.RecordedOnUtc);
        Assert.Equal(Utc(10), result.Value.PaidOnUtc);
    }

    [Fact]
    public async Task Record_ShouldRejectNonCompletedBooking()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = AddBooking(familiesDb, caregiver.Id);
        booking.MarkAsPaid("order-1", "tx-1", Utc(2));
        booking.AcceptByCaregiver(Utc(2));
        await familiesDb.SaveChangesAsync();

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var result =
            await handler.Handle(
                new RecordCaregiverPayoutCommand(
                    booking.Id.Value,
                    UserId.New(),
                    "BANK-2026-000123",
                    "Transfer advice 000123",
                    "October weekly payout",
                    Utc(10)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.Ineligible, result.Error);
    }

    [Fact]
    public async Task Record_ShouldFailClosed_WhenNoEffectivePolicy()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(dbContext, caregiver.Id);

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(null));

        var result =
            await handler.Handle(
                new RecordCaregiverPayoutCommand(
                    booking.Id.Value,
                    UserId.New(),
                    "BANK-2026-000123",
                    "Transfer advice 000123",
                    "October weekly payout",
                    Utc(10)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(CaregiverPayoutPolicyErrors.Missing, result.Error);
    }

    [Fact]
    public async Task Record_ShouldRejectBooking_BeforeDelayPasses()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id, completedDay: 9);
        await AddPayoutAccountAsync(dbContext, caregiver.Id);

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var result =
            await handler.Handle(
                new RecordCaregiverPayoutCommand(
                    booking.Id.Value,
                    UserId.New(),
                    "BANK-2026-000123",
                    "Transfer advice 000123",
                    "October weekly payout",
                    Utc(10)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.Ineligible, result.Error);
    }

    [Fact]
    public async Task Record_ShouldRejectBooking_WithoutPayoutAccount()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var result =
            await handler.Handle(
                new RecordCaregiverPayoutCommand(
                    booking.Id.Value,
                    UserId.New(),
                    "BANK-2026-000123",
                    "Transfer advice 000123",
                    "October weekly payout",
                    Utc(10)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(PayoutAccountErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Record_ShouldRejectDuplicatePaidPayout()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(dbContext, caregiver.Id);

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var command = new RecordCaregiverPayoutCommand(
            booking.Id.Value,
            UserId.New(),
            "BANK-2026-000123",
            "Transfer advice 000123",
            "October weekly payout",
            Utc(10));

        Assert.True((await handler.Handle(command, default)).IsSuccess);

        var duplicate = await handler.Handle(command, default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.Conflict, duplicate.Error);
    }

    [Fact]
    public async Task Record_ShouldAllowNewRow_AfterPreviousFailed()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(dbContext, caregiver.Id);

        var recordHandler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var first = await recordHandler.Handle(
            new RecordCaregiverPayoutCommand(
                booking.Id.Value,
                UserId.New(),
                "BANK-2026-000123",
                "Transfer advice 000123",
                "October weekly payout",
                Utc(10)),
            default);

        Assert.True(first.IsSuccess);

        var failed = await new MarkCaregiverPayoutFailedCommandHandler(dbContext).Handle(
            new MarkCaregiverPayoutFailedCommand(
                first.Value.PayoutId,
                UserId.New(),
                "Bank rejected the transfer",
                Utc(11)),
            default);

        Assert.True(failed.IsSuccess);
        Assert.Equal("Failed", failed.Value.Status);
        Assert.Equal("Bank rejected the transfer", failed.Value.FailureReason);
        Assert.Null(failed.Value.PaidOnUtc);
        Assert.Equal(Utc(11), failed.Value.FailedOnUtc);

        var retry = await recordHandler.Handle(
            new RecordCaregiverPayoutCommand(
                booking.Id.Value,
                UserId.New(),
                "BANK-2026-000124",
                "Transfer advice 000124",
                "October weekly payout retry",
                Utc(12)),
            default);

        Assert.True(retry.IsSuccess);
        Assert.Equal(
            2,
            await dbContext.Payouts.CountAsync());
    }

    [Fact]
    public async Task MarkFailed_ShouldRejectUnknownPayout()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();

        var result =
            await new MarkCaregiverPayoutFailedCommandHandler(dbContext).Handle(
                new MarkCaregiverPayoutFailedCommand(
                    Guid.CreateVersion7(),
                    UserId.New(),
                    "Bank rejected the transfer",
                    Utc(11)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task MarkFailed_ShouldRejectNonPaidPayout()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(dbContext);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(dbContext, caregiver.Id);

        var recordHandler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var recorded = await recordHandler.Handle(
            new RecordCaregiverPayoutCommand(
                booking.Id.Value,
                UserId.New(),
                "BANK-2026-000123",
                "Transfer advice 000123",
                "October weekly payout",
                Utc(10)),
            default);

        var failedHandler = new MarkCaregiverPayoutFailedCommandHandler(dbContext);
        var actor = UserId.New();

        Assert.True((await failedHandler.Handle(
            new MarkCaregiverPayoutFailedCommand(
                recorded.Value.PayoutId,
                actor,
                "Bank rejected the transfer",
                Utc(11)),
            default)).IsSuccess);

        var repeat = await failedHandler.Handle(
            new MarkCaregiverPayoutFailedCommand(
                recorded.Value.PayoutId,
                actor,
                "Again",
                Utc(12)),
            default);

        Assert.True(repeat.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.InvalidState, repeat.Error);
    }

    [Fact]
    public async Task List_ShouldPageFilterAndOrderByRecordingTime()
    {
        using CaregiversDbContext dbContext = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver first = await AddCaregiverAsync(dbContext);
        Caregiver second = await AddCaregiverAsync(dbContext);
        Booking firstBooking = await AddCompletedBookingAsync(familiesDb, first.Id);
        Booking secondBooking = await AddCompletedBookingAsync(familiesDb, second.Id);
        await AddPayoutAccountAsync(dbContext, first.Id);
        await AddPayoutAccountAsync(dbContext, second.Id);

        var recordHandler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(0, 2)));

        await recordHandler.Handle(
            new RecordCaregiverPayoutCommand(
                firstBooking.Id.Value,
                UserId.New(),
                "BANK-2026-000123",
                "Transfer advice 000123",
                "Payout one",
                Utc(10)),
            default);

        var secondRecorded = await recordHandler.Handle(
            new RecordCaregiverPayoutCommand(
                secondBooking.Id.Value,
                UserId.New(),
                "BANK-2026-000124",
                "Transfer advice 000124",
                "Payout two",
                Utc(11)),
            default);

        await new MarkCaregiverPayoutFailedCommandHandler(dbContext).Handle(
            new MarkCaregiverPayoutFailedCommand(
                secondRecorded.Value.PayoutId,
                UserId.New(),
                "Bank rejected the transfer",
                Utc(12)),
            default);

        var listHandler = new GetCaregiverPayoutsQueryHandler(dbContext);

        var page = await listHandler.Handle(
            new GetCaregiverPayoutsQuery(null, null, 1, 1),
            default);

        Assert.True(page.IsSuccess);
        Assert.Equal(2, page.Value.TotalCount);
        Assert.Equal(2, page.Value.TotalPages);
        Assert.Single(page.Value.Items);

        var paid = await listHandler.Handle(
            new GetCaregiverPayoutsQuery(PayoutStatus.Paid, null, 1, 20),
            default);

        Assert.True(paid.IsSuccess);
        var paidItem = Assert.Single(paid.Value.Items);
        Assert.Equal(firstBooking.Id.Value, paidItem.BookingId);

        var byCaregiver = await listHandler.Handle(
            new GetCaregiverPayoutsQuery(null, second.Id.Value, 1, 20),
            default);

        Assert.True(byCaregiver.IsSuccess);
        Assert.Equal("Failed", Assert.Single(byCaregiver.Value.Items).Status);

        var detail = await new GetCaregiverPayoutQueryHandler(dbContext).Handle(
            new GetCaregiverPayoutQuery(secondRecorded.Value.PayoutId),
            default);

        Assert.True(detail.IsSuccess);
        Assert.Equal("****0002", detail.Value.MaskedIban);

        var missing = await new GetCaregiverPayoutQueryHandler(dbContext).Handle(
            new GetCaregiverPayoutQuery(Guid.CreateVersion7()),
            default);

        Assert.True(missing.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.NotFound, missing.Error);
    }

    [Fact]
    public async Task EligibleSelector_ShouldReturnEligibleBookingWithNamesAndFee()
    {
        using CaregiversDbContext inner = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(inner);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(inner, caregiver.Id);

        var handler = CreateEligibleSelectorHandler(inner, familiesDb);

        var result = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Equal(1, result.Value.TotalCount);
        Assert.Equal(1, result.Value.TotalPages);
        EligibleCaregiverPayoutBookingResponse item =
            Assert.Single(result.Value.Items);
        Assert.Equal(booking.Id.Value, item.BookingId);
        Assert.Equal(caregiver.Id.Value, item.CaregiverId);
        Assert.Equal("محمد أحمد", item.ArabicFullName);
        Assert.Equal("Mohamed Ahmed", item.EnglishFullName);
        // completed Utc(5) + 72h delay.
        Assert.Equal(Utc(5), item.CompletedOnUtc);
        Assert.Equal(Utc(8), item.EligibleOnUtc);
        Assert.Equal(150m, item.CaregiverAmount);
        Assert.Equal("EGP", item.Currency);

        // Read-only: the selector never stages a booking, account or payout write.
        Assert.False(inner.ChangeTracker.HasChanges());
        Assert.False(familiesDb.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task EligibleSelector_ShouldExcludeIneligibleBookings()
    {
        using CaregiversDbContext inner = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver included = await AddCaregiverAsync(inner);
        await AddPayoutAccountAsync(inner, included.Id);
        Booking includedBooking = await AddCompletedBookingAsync(
            familiesDb,
            included.Id);

        Caregiver pendingAccount = await AddCaregiverAsync(inner);
        await AddPayoutAccountInStatusAsync(
            inner,
            pendingAccount.Id,
            PayoutAccountStatus.Pending);
        await AddCompletedBookingAsync(familiesDb, pendingAccount.Id);

        Caregiver alreadyPaid = await AddCaregiverAsync(inner);
        await AddPayoutAccountAsync(inner, alreadyPaid.Id);
        Booking paidBooking = await AddCompletedBookingAsync(
            familiesDb,
            alreadyPaid.Id);
        inner.Payouts.Add(RecordPayout(paidBooking, alreadyPaid.Id, Utc(9)));
        await inner.SaveChangesAsync();

        Caregiver notDue = await AddCaregiverAsync(inner);
        await AddPayoutAccountAsync(inner, notDue.Id);
        await AddCompletedBookingAsync(familiesDb, notDue.Id, completedDay: 19);

        Caregiver noAccount = await AddCaregiverAsync(inner);
        await AddCompletedBookingAsync(familiesDb, noAccount.Id);

        // Paid but never completed: the selector only considers Completed bookings.
        Caregiver notCompleted = await AddCaregiverAsync(inner);
        await AddPayoutAccountAsync(inner, notCompleted.Id);
        Booking inProgress = AddBooking(familiesDb, notCompleted.Id);
        inProgress.MarkAsPaid("order-2", "tx-2", Utc(2));
        inProgress.AcceptByCaregiver(Utc(2));
        inProgress.StartVisit(Utc(4));
        await familiesDb.SaveChangesAsync();

        var handler = CreateEligibleSelectorHandler(inner, familiesDb);

        var result = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
        EligibleCaregiverPayoutBookingResponse item =
            Assert.Single(result.Value.Items);
        Assert.Equal(includedBooking.Id.Value, item.BookingId);
        Assert.Equal(included.Id.Value, item.CaregiverId);
    }

    [Fact]
    public async Task EligibleSelector_ShouldKeepFailedPayoutBookingEligibleForRetry()
    {
        using CaregiversDbContext inner = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPayoutAccountAsync(inner, caregiver.Id);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);

        CaregiverPayout failed = RecordPayout(booking, caregiver.Id, Utc(9));
        failed.MarkFailed("Bank rejected the transfer", UserId.New(), Utc(10));
        inner.Payouts.Add(failed);
        await inner.SaveChangesAsync();

        var handler = CreateEligibleSelectorHandler(inner, familiesDb);

        var result = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(booking.Id.Value, Assert.Single(result.Value.Items).BookingId);
    }

    [Fact]
    public async Task EligibleSelector_ShouldFailClosed_WhenNoEffectivePolicy()
    {
        using CaregiversDbContext inner = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(inner);
        await AddPayoutAccountAsync(inner, caregiver.Id);
        await AddCompletedBookingAsync(familiesDb, caregiver.Id);

        var handler = new GetEligibleCaregiverPayoutBookingsQueryHandler(
            new FixedHeaderCaregiversDbContext(inner),
            familiesDb,
            new FixedPolicyReader(null),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CaregiverPayoutPolicyErrors.Missing, result.Error);
    }

    [Fact]
    public async Task EligibleSelector_ShouldPageOrderAndFilterByCaregiver()
    {
        using CaregiversDbContext inner = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver first = await AddCaregiverAsync(inner);
        Caregiver second = await AddCaregiverAsync(inner);
        await AddPayoutAccountAsync(inner, first.Id);
        await AddPayoutAccountAsync(inner, second.Id);

        Booking firstEarly = await AddCompletedBookingAsync(
            familiesDb,
            first.Id,
            completedDay: 5);
        Booking firstLate = await AddCompletedBookingAsync(
            familiesDb,
            first.Id,
            completedDay: 7);
        Booking secondMiddle = await AddCompletedBookingAsync(
            familiesDb,
            second.Id,
            completedDay: 6);

        var handler = CreateEligibleSelectorHandler(inner, familiesDb);

        var firstPage = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(Page: 1, PageSize: 2),
            default);

        Assert.True(firstPage.IsSuccess);
        Assert.Equal(3, firstPage.Value.TotalCount);
        Assert.Equal(2, firstPage.Value.TotalPages);
        Assert.Equal(
            new[] { firstEarly.Id.Value, secondMiddle.Id.Value },
            firstPage.Value.Items.Select(i => i.BookingId).ToArray());

        var secondPage = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(Page: 2, PageSize: 2),
            default);

        Assert.True(secondPage.IsSuccess);
        Assert.Equal(
            firstLate.Id.Value,
            Assert.Single(secondPage.Value.Items).BookingId);

        var filtered = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(second.Id.Value),
            default);

        Assert.True(filtered.IsSuccess);
        Assert.Equal(1, filtered.Value.TotalCount);
        Assert.Equal(
            secondMiddle.Id.Value,
            Assert.Single(filtered.Value.Items).BookingId);

        // Page and page size are clamped, not rejected.
        var clamped = await handler.Handle(
            new GetEligibleCaregiverPayoutBookingsQuery(Page: 0, PageSize: 0),
            default);

        Assert.True(clamped.IsSuccess);
        Assert.Equal(1, clamped.Value.Page);
        Assert.Equal(1, clamped.Value.PageSize);
        Assert.Single(clamped.Value.Items);
    }

    [Fact]
    public void EligibleSelectorResponse_ShouldNotExposePayoutAccountOrIbanFields()
    {
        string[] propertyNames = typeof(EligibleCaregiverPayoutBookingResponse)
            .GetProperties()
            .Select(p => p.Name)
            .ToArray();

        Assert.All(
            propertyNames,
            name => Assert.DoesNotContain(
                "iban",
                name,
                StringComparison.OrdinalIgnoreCase));
        Assert.All(
            propertyNames,
            name => Assert.DoesNotContain(
                "bank",
                name,
                StringComparison.OrdinalIgnoreCase));
    }

    private static GetEligibleCaregiverPayoutBookingsQueryHandler
        CreateEligibleSelectorHandler(
            CaregiversDbContext inner,
            FamiliesDbContext familiesDb)
    {
        return new GetEligibleCaregiverPayoutBookingsQueryHandler(
            new FixedHeaderCaregiversDbContext(inner),
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)),
            new FixedClock(Utc(20)));
    }

    private static CaregiverPayout RecordPayout(
        Booking booking,
        CaregiverId caregiverId,
        DateTime recordedOnUtc) =>
        CaregiverPayout.Record(
            booking.Id,
            caregiverId,
            booking.PriceSnapshot.BaseCaregiverFee,
            booking.PriceSnapshot.PlatformFeeAmount,
            booking.PriceSnapshot.Currency,
            2,
            booking.PriceSnapshot.PlatformChargeRuleVersion,
            "NBE",
            "****0002",
            "BANK-2026-000123",
            "Transfer advice 000123",
            "October weekly payout",
            UserId.New(),
            recordedOnUtc);

    private static async Task<Caregiver> AddCaregiverAsync(
        CaregiversDbContext dbContext)
    {
        Caregiver caregiver =
            Caregiver.Create(UserId.New(), CaregiverType.Medical);

        dbContext.Caregivers.Add(caregiver);
        await dbContext.SaveChangesAsync();

        return caregiver;
    }

    private static Booking AddBooking(
        FamiliesDbContext familiesDb,
        CaregiverId caregiverId)
    {
        Booking booking = Booking.Create(
            new FamilyId(Guid.CreateVersion7()),
            UserId.New(),
            new ElderlyId(Guid.CreateVersion7()),
            caregiverId,
            BookingCaregiverType.Medical,
            BookingShiftType.HomeVisit,
            DateOnly.FromDateTime(Utc(4)),
            new TimeOnly(9, 0),
            new TimeOnly(10, 0),
            "12 Corniche, Cairo",
            null,
            BookingPriceSnapshot.Calculate(150m, 15m, 14m, 1),
            Utc(3),
            DateOnly.FromDateTime(Utc(1)),
            Utc(1));

        familiesDb.Bookings.Add(booking);

        return booking;
    }

    private static async Task<Booking> AddCompletedBookingAsync(
        FamiliesDbContext familiesDb,
        CaregiverId caregiverId,
        int completedDay = 5)
    {
        Booking booking = AddBooking(familiesDb, caregiverId);
        booking.MarkAsPaid("order-1", "tx-1", Utc(2));
        booking.AcceptByCaregiver(Utc(2));
        booking.StartVisit(Utc(4));
        booking.CompleteVisit("Visit completed", Utc(completedDay));
        await familiesDb.SaveChangesAsync();

        return booking;
    }

    private static async Task AddPayoutAccountAsync(
        CaregiversDbContext dbContext,
        CaregiverId caregiverId)
    {
        var account = CaregiverPayoutAccount.Create(
            caregiverId,
            "Mohamed Ahmed",
            "NBE",
            "v1.test.ciphertext",
            "0002");

        account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);
        dbContext.PayoutAccounts.Add(account);
        await dbContext.SaveChangesAsync();
    }

    private static async Task AddPayoutAccountInStatusAsync(
        CaregiversDbContext dbContext,
        CaregiverId caregiverId,
        PayoutAccountStatus status)
    {
        var account = CaregiverPayoutAccount.Create(
            caregiverId,
            "Mohamed Ahmed",
            "NBE",
            "v1.test.ciphertext",
            "0002");

        switch (status)
        {
            case PayoutAccountStatus.Verified:
                account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);
                break;
            case PayoutAccountStatus.Rejected:
                account.Reject(UserId.New(), "Name mismatch", 1, Utc(2));
                break;
            case PayoutAccountStatus.Revoked:
                account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);
                account.Revoke(UserId.New(), "Fraud suspected", 2, Utc(3));
                break;
        }

        dbContext.PayoutAccounts.Add(account);
        await dbContext.SaveChangesAsync();
    }

    [Fact]
    public async Task Record_ShouldRejectUnverifiedPayoutAccount()
    {
        foreach (var status in new[]
                 {
                     PayoutAccountStatus.Pending,
                     PayoutAccountStatus.Rejected,
                     PayoutAccountStatus.Revoked
                 })
        {
            using CaregiversDbContext dbContext = CreateCaregiversDbContext();
            using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

            Caregiver caregiver = await AddCaregiverAsync(dbContext);
            Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
            await AddPayoutAccountInStatusAsync(dbContext, caregiver.Id, status);

            var handler = new RecordCaregiverPayoutCommandHandler(
                dbContext,
                familiesDb,
                new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

            var result =
                await handler.Handle(
                    new RecordCaregiverPayoutCommand(
                        booking.Id.Value,
                        UserId.New(),
                        "BANK-2026-000123",
                        "Transfer advice 000123",
                        "October weekly payout",
                        Utc(10)),
                    default);

            Assert.True(result.IsFailure);
            Assert.Equal(CaregiverPayoutErrors.AccountNotVerified, result.Error);
        }
    }

    private static CaregiversDbContext CreateCaregiversDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }

    private static FamiliesDbContext CreateFamiliesDbContext()
    {
        DbContextOptions<FamiliesDbContext> options =
            new DbContextOptionsBuilder<FamiliesDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new FamiliesDbContext(options);
    }

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Record_ShouldReturnConflict_WhenPaidConstraintViolated()
    {
        using CaregiversDbContext inner = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(inner);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(inner, caregiver.Id);

        var dbContext = new ThrowingSaveCaregiversDbContext(
            inner,
            () => new DbUpdateException(
                "duplicate key value violates unique constraint " +
                "\"ux_caregiver_payouts_booking_paid\"",
                innerException: null));

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        var result =
            await handler.Handle(
                new RecordCaregiverPayoutCommand(
                    booking.Id.Value,
                    UserId.New(),
                    "BANK-2026-000123",
                    "Transfer advice 000123",
                    "October weekly payout",
                    Utc(10)),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.Conflict, result.Error);
    }

    [Fact]
    public async Task Record_ShouldPropagateUnrelatedDatabaseFailures()
    {
        using CaregiversDbContext inner = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(inner);
        Booking booking = await AddCompletedBookingAsync(familiesDb, caregiver.Id);
        await AddPayoutAccountAsync(inner, caregiver.Id);

        var dbContext = new ThrowingSaveCaregiversDbContext(
            inner,
            () => new DbUpdateException("connection failure", innerException: null));

        var handler = new RecordCaregiverPayoutCommandHandler(
            dbContext,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)));

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.Handle(
            new RecordCaregiverPayoutCommand(
                booking.Id.Value,
                UserId.New(),
                "BANK-2026-000123",
                "Transfer advice 000123",
                "October weekly payout",
                Utc(10)),
            default));
    }

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FixedHeaderCaregiversDbContext(
        ICaregiversDbContext inner) : ICaregiversDbContext
    {
        public DbSet<Caregiver> Caregivers => inner.Caregivers;
        public DbSet<CaregiverRating> CaregiverRatings => inner.CaregiverRatings;
        public DbSet<CaregiverRatingModerationAction> RatingModerationActions => inner.RatingModerationActions;
        public DbSet<CaregiverReviewNote> ReviewNotes => inner.ReviewNotes;
        public DbSet<Service> Services => inner.Services;
        public DbSet<Language> Languages => inner.Languages;
        public DbSet<Bank> Banks => inner.Banks;
        public DbSet<CaregiverPayoutAccount> PayoutAccounts => inner.PayoutAccounts;
        public DbSet<CaregiverPayoutAccountReview> PayoutAccountReviews => inner.PayoutAccountReviews;
        public DbSet<CaregiverPayout> Payouts => inner.Payouts;
        public DbSet<Governorate> Governorates => inner.Governorates;
        public DbSet<City> Cities => inner.Cities;
        public DbSet<Area> Areas => inner.Areas;
        public DbSet<Specialization> Specializations => inner.Specializations;
        public DbSet<ProfessionalTitle> ProfessionalTitles => inner.ProfessionalTitles;
        public DbSet<AcademicDegree> AcademicDegrees => inner.AcademicDegrees;

        public Task<IReadOnlyList<AdminCaregiverListItem>> GetAdminCaregiversAsync(
            int page, int pageSize, int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiversAsync(page, pageSize, status, type, cancellationToken);

        public Task<int> CountAdminCaregiversAsync(
            int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.CountAdminCaregiversAsync(status, type, cancellationToken);

        public Task<(IReadOnlyList<AdminCaregiverLookupItem> Items, int TotalCount)> GetAdminCaregiverLookupAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiverLookupAsync(search, page, pageSize, cancellationToken);

        public Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
            string? search, int? type, int? gender, Guid? governorateId, Guid? cityId, Guid? areaId, Guid? specializationId,
            int? availability, decimal? minPrice, decimal? maxPrice, decimal? minRating,
            int? minExperienceYears, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveCaregiversAsync(
                search, type, gender, governorateId, cityId, areaId, specializationId, availability,
                minPrice, maxPrice, minRating, minExperienceYears, page, pageSize,
                cancellationToken);

        public Task<(IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount)> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveMedicalCaregiversAsync(search, page, pageSize, cancellationToken);

        // The in-memory provider cannot run the production raw-SQL identity join,
        // so the safe display name is supplied deterministically for tests.
        public Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CaregiverUserHeader?>(
                new CaregiverUserHeader(
                    userId.Value,
                    "محمد أحمد",
                    "Mohamed Ahmed",
                    null,
                    null));

        public Task<string?> GetDiscoverableCaregiverAvatarKeyAsync(
            CaregiverId caregiverId,
            CancellationToken cancellationToken = default) =>
            inner.GetDiscoverableCaregiverAvatarKeyAsync(caregiverId, cancellationToken);

        public Task<IReadOnlyList<TopRatedCaregiverCard>> GetTopRatedCaregiversAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetTopRatedCaregiversAsync(cancellationToken);

        public Task<PayoutRecordSaveResult> SavePayoutIfAccountRevisionVerifiedAsync(
            CaregiverId caregiverId, int expectedRevision, CaregiverPayout payout,
            CancellationToken cancellationToken = default) =>
            inner.SavePayoutIfAccountRevisionVerifiedAsync(
                caregiverId, expectedRevision, payout, cancellationToken);

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            inner.SaveChangesAsync(cancellationToken);
    }

    private sealed class ThrowingSaveCaregiversDbContext(
        ICaregiversDbContext inner,
        Func<DbUpdateException> failure) : ICaregiversDbContext
    {
        public DbSet<Caregiver> Caregivers => inner.Caregivers;
        public DbSet<CaregiverRating> CaregiverRatings => inner.CaregiverRatings;
        public DbSet<CaregiverRatingModerationAction> RatingModerationActions => inner.RatingModerationActions;
        public DbSet<CaregiverReviewNote> ReviewNotes => inner.ReviewNotes;
        public DbSet<Service> Services => inner.Services;
        public DbSet<Language> Languages => inner.Languages;
        public DbSet<Bank> Banks => inner.Banks;
        public DbSet<CaregiverPayoutAccount> PayoutAccounts => inner.PayoutAccounts;
        public DbSet<CaregiverPayoutAccountReview> PayoutAccountReviews => inner.PayoutAccountReviews;
        public DbSet<CaregiverPayout> Payouts => inner.Payouts;
        public DbSet<Governorate> Governorates => inner.Governorates;
        public DbSet<City> Cities => inner.Cities;
        public DbSet<Area> Areas => inner.Areas;
        public DbSet<Specialization> Specializations => inner.Specializations;
        public DbSet<ProfessionalTitle> ProfessionalTitles => inner.ProfessionalTitles;
        public DbSet<AcademicDegree> AcademicDegrees => inner.AcademicDegrees;

        public Task<IReadOnlyList<AdminCaregiverListItem>> GetAdminCaregiversAsync(
            int page, int pageSize, int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiversAsync(page, pageSize, status, type, cancellationToken);

        public Task<int> CountAdminCaregiversAsync(
            int? status, int? type,
            CancellationToken cancellationToken = default) =>
            inner.CountAdminCaregiversAsync(status, type, cancellationToken);

        public Task<(IReadOnlyList<AdminCaregiverLookupItem> Items, int TotalCount)> GetAdminCaregiverLookupAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.GetAdminCaregiverLookupAsync(search, page, pageSize, cancellationToken);

        public Task<(IReadOnlyList<CaregiverSearchCardResponse> Items, int TotalCount)> SearchActiveCaregiversAsync(
            string? search, int? type, int? gender, Guid? governorateId, Guid? cityId, Guid? areaId, Guid? specializationId,
            int? availability, decimal? minPrice, decimal? maxPrice, decimal? minRating,
            int? minExperienceYears, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveCaregiversAsync(
                search, type, gender, governorateId, cityId, areaId, specializationId, availability,
                minPrice, maxPrice, minRating, minExperienceYears, page, pageSize,
                cancellationToken);

        public Task<(IReadOnlyList<MedicalCaregiverRecipientItem> Items, int TotalCount)> SearchActiveMedicalCaregiversAsync(
            string? search, int page, int pageSize,
            CancellationToken cancellationToken = default) =>
            inner.SearchActiveMedicalCaregiversAsync(search, page, pageSize, cancellationToken);

        public Task<CaregiverUserHeader?> GetCaregiverUserHeaderAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            inner.GetCaregiverUserHeaderAsync(userId, cancellationToken);

        public Task<string?> GetDiscoverableCaregiverAvatarKeyAsync(
            CaregiverId caregiverId,
            CancellationToken cancellationToken = default) =>
            inner.GetDiscoverableCaregiverAvatarKeyAsync(caregiverId, cancellationToken);

        public Task<IReadOnlyList<TopRatedCaregiverCard>> GetTopRatedCaregiversAsync(
            CancellationToken cancellationToken = default) =>
            inner.GetTopRatedCaregiversAsync(cancellationToken);

        public Task<PayoutRecordSaveResult> SavePayoutIfAccountRevisionVerifiedAsync(
            CaregiverId caregiverId, int expectedRevision, CaregiverPayout payout,
            CancellationToken cancellationToken = default) =>
            throw failure();

        public Task<int> SaveChangesAsync(
            CancellationToken cancellationToken = default) =>
            throw failure();
    }

    private sealed class FixedPolicyReader(
        CaregiverPayoutPolicyRates? rates) : ICaregiverPayoutPolicyReader
    {
        public Task<CaregiverPayoutPolicyRates?> GetEffectiveAsync(
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(rates);

        public Task<IReadOnlyList<CaregiverPayoutPolicyHistoryItem>> GetHistoryAsync(
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CaregiverPayoutPolicyHistoryItem>>([]);
    }
}
