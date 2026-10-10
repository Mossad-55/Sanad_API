using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Payouts;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverPayoutReadTests
{
    [Fact]
    public async Task Summary_ShouldSumTransferredFailedAndEligibleUnpaid()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);
        await AddPayoutAccountInStatusAsync(
            caregiversDb,
            caregiver.Id,
            PayoutAccountStatus.Verified);

        Booking paidBooking = await AddCompletedBookingAsync(
            familiesDb,
            caregiver.Id);
        Booking failedBooking = await AddCompletedBookingAsync(
            familiesDb,
            caregiver.Id);
        Booking unpaidBooking = await AddCompletedBookingAsync(
            familiesDb,
            caregiver.Id);

        caregiversDb.Payouts.Add(
            RecordPayout(paidBooking, caregiver.Id, Utc(9)));
        caregiversDb.Payouts.Add(
            RecordFailedPayout(
                failedBooking,
                caregiver.Id,
                Utc(10),
                Utc(11)));
        await caregiversDb.SaveChangesAsync();

        var handler = new GetCaregiverPayoutSummaryQueryHandler(
            caregiversDb,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetCaregiverPayoutSummaryQuery(caregiver.Id.Value),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal("EGP", result.Value.Currency);
        // Failed payout rows do not clear the debt: failed + never-recorded
        // bookings both count as eligible unpaid amounts.
        Assert.Equal(300m, result.Value.UnpaidAmount);
        Assert.Equal(150m, result.Value.TransferredAmount);
        Assert.Equal(150m, result.Value.FailedAmount);
        // Eligibility = max(completedOnUtc Utc(5), paidOnUtc Utc(2)) + 72h.
        Assert.Equal(Utc(8), result.Value.NextExpectedPayoutOnUtc);
    }

    [Fact]
    public async Task Summary_ShouldReturnNullNextExpected_WithoutVerifiedAccount()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);
        await AddPayoutAccountInStatusAsync(
            caregiversDb,
            caregiver.Id,
            PayoutAccountStatus.Pending);

        await AddCompletedBookingAsync(familiesDb, caregiver.Id);

        var handler = new GetCaregiverPayoutSummaryQueryHandler(
            caregiversDb,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetCaregiverPayoutSummaryQuery(caregiver.Id.Value),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(150m, result.Value.UnpaidAmount);
        Assert.Null(result.Value.NextExpectedPayoutOnUtc);
    }

    [Fact]
    public async Task Summary_ShouldFailClosed_WhenNoEffectivePolicy()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);
        await AddCompletedBookingAsync(familiesDb, caregiver.Id);

        var handler = new GetCaregiverPayoutSummaryQueryHandler(
            caregiversDb,
            familiesDb,
            new FixedPolicyReader(null),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetCaregiverPayoutSummaryQuery(caregiver.Id.Value),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(CaregiverPayoutPolicyErrors.Missing, result.Error);
    }

    [Fact]
    public async Task Summary_ShouldExcludeNotYetEligibleBookings_FromUnpaidAmount()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);
        await AddPayoutAccountInStatusAsync(
            caregiversDb,
            caregiver.Id,
            PayoutAccountStatus.Verified);

        await AddCompletedBookingAsync(familiesDb, caregiver.Id);

        // Utc(6) is before the Utc(8) eligibility boundary (72h delay).
        var handler = new GetCaregiverPayoutSummaryQueryHandler(
            caregiversDb,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)),
            new FixedClock(Utc(6)));

        var result = await handler.Handle(
            new GetCaregiverPayoutSummaryQuery(caregiver.Id.Value),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.UnpaidAmount);
        Assert.Equal("EGP", result.Value.Currency);
        Assert.Equal(Utc(8), result.Value.NextExpectedPayoutOnUtc);
    }

    [Fact]
    public async Task Summary_ShouldReturnHonestEmptyState_WhenNothingExists()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);

        var handler = new GetCaregiverPayoutSummaryQueryHandler(
            caregiversDb,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetCaregiverPayoutSummaryQuery(caregiver.Id.Value),
            default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Currency);
        Assert.Equal(0m, result.Value.UnpaidAmount);
        Assert.Equal(0m, result.Value.TransferredAmount);
        Assert.Equal(0m, result.Value.FailedAmount);
        Assert.Null(result.Value.NextExpectedPayoutOnUtc);
    }

    [Fact]
    public async Task Summary_ShouldIgnoreOtherCaregiversRows()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);
        Caregiver other = await AddCaregiverAsync(caregiversDb);

        Booking otherBooking = await AddCompletedBookingAsync(
            familiesDb,
            other.Id);
        caregiversDb.Payouts.Add(RecordPayout(otherBooking, other.Id, Utc(9)));
        await caregiversDb.SaveChangesAsync();

        var handler = new GetCaregiverPayoutSummaryQueryHandler(
            caregiversDb,
            familiesDb,
            new FixedPolicyReader(new CaregiverPayoutPolicyRates(72, 2)),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetCaregiverPayoutSummaryQuery(caregiver.Id.Value),
            default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Currency);
        Assert.Equal(0m, result.Value.UnpaidAmount);
        Assert.Equal(0m, result.Value.TransferredAmount);
        Assert.Null(result.Value.NextExpectedPayoutOnUtc);
    }

    [Fact]
    public async Task List_ShouldFilterByStatusMapFieldsAndResolveBankNames()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);

        Booking paidBooking = await AddCompletedBookingAsync(
            familiesDb,
            caregiver.Id);
        Booking failedBooking = await AddCompletedBookingAsync(
            familiesDb,
            caregiver.Id);

        CaregiverPayout paid = RecordPayout(
            paidBooking,
            caregiver.Id,
            Utc(9));
        CaregiverPayout failed = RecordFailedPayout(
            failedBooking,
            caregiver.Id,
            Utc(10),
            Utc(11));

        caregiversDb.Banks.Add(
            Bank.Create("NBE", "البنك الاهلي المصري", "National Bank of Egypt"));
        caregiversDb.Payouts.Add(paid);
        caregiversDb.Payouts.Add(failed);
        await caregiversDb.SaveChangesAsync();

        var handler = new GetCaregiverPayoutListQueryHandler(caregiversDb);

        var paidOnly = await handler.Handle(
            new GetCaregiverPayoutListQuery(caregiver.Id.Value, PayoutStatus.Paid),
            default);

        Assert.True(paidOnly.IsSuccess);
        Assert.Equal(1, paidOnly.Value.TotalCount);
        CaregiverPayoutListItemResponse item = paidOnly.Value.Items.Single();
        Assert.Equal(paid.Id.Value, item.Id);
        Assert.Equal(150m, item.Amount);
        Assert.Equal("EGP", item.Currency);
        Assert.Equal("Paid", item.Status);
        Assert.Equal("البنك الاهلي المصري", item.BankNameArabic);
        Assert.Equal("National Bank of Egypt", item.BankNameEnglish);
        Assert.Equal("****0002", item.MaskedIban);
        Assert.Equal("BANK-2026-000123", item.TransferReference);
        Assert.Equal(Utc(9), item.CreatedOnUtc);
        Assert.Equal(Utc(9), item.TransferredOnUtc);
        Assert.Null(item.FailureReason);

        var all = await handler.Handle(
            new GetCaregiverPayoutListQuery(caregiver.Id.Value),
            default);

        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value.TotalCount);
        Assert.Equal("Failed", all.Value.Items[0].Status);
        Assert.Null(all.Value.Items[0].TransferredOnUtc);
        Assert.Equal("BANK-000999", all.Value.Items[0].TransferReference);
        Assert.Equal("Transfer declined by bank", all.Value.Items[0].FailureReason);

        // Page size is clamped to the documented 1–100 bounds.
        var clamped = await handler.Handle(
            new GetCaregiverPayoutListQuery(
                caregiver.Id.Value,
                Page: 0,
                PageSize: 0),
            default);

        Assert.True(clamped.IsSuccess);
        Assert.Equal(1, clamped.Value.Page);
        Assert.Equal(1, clamped.Value.PageSize);
        Assert.Single(clamped.Value.Items);
    }

    [Fact]
    public async Task List_ShouldScopeRowsToTheRequestedCaregiver()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);
        Caregiver other = await AddCaregiverAsync(caregiversDb);

        Booking otherBooking = await AddCompletedBookingAsync(
            familiesDb,
            other.Id);
        caregiversDb.Payouts.Add(RecordPayout(otherBooking, other.Id, Utc(9)));
        await caregiversDb.SaveChangesAsync();

        var handler = new GetCaregiverPayoutListQueryHandler(caregiversDb);

        var result = await handler.Handle(
            new GetCaregiverPayoutListQuery(caregiver.Id.Value),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalCount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task Detail_ShouldReturnBookingItemsAndPreserveNetEqualsGross()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);

        Booking booking = await AddCompletedBookingAsync(
            familiesDb,
            caregiver.Id);

        CaregiverPayout payout = RecordPayout(booking, caregiver.Id, Utc(9));
        caregiversDb.Payouts.Add(payout);
        await caregiversDb.SaveChangesAsync();

        // Structural invariant: no payout-level fee is ever deducted.
        Assert.Equal(payout.GrossAmount, payout.NetAmount);

        var handler = new GetCaregiverPayoutDetailQueryHandler(caregiversDb);

        var result = await handler.Handle(
            new GetCaregiverPayoutDetailQuery(
                caregiver.Id.Value,
                payout.Id.Value),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(payout.Id.Value, result.Value.Id);
        Assert.Equal(150m, result.Value.Amount);
        Assert.Equal("Paid", result.Value.Status);

        CaregiverPayoutBookingItemResponse bookingItem =
            Assert.Single(result.Value.BookingItems);
        Assert.Equal(booking.Id.Value, bookingItem.BookingId);
        Assert.Equal(150m, bookingItem.CaregiverAmount);
        Assert.Equal("EGP", bookingItem.Currency);
    }

    [Fact]
    public async Task Detail_ShouldReturnNotFound_ForAnotherCaregiversPayout()
    {
        using CaregiversDbContext caregiversDb = CreateCaregiversDbContext();
        using FamiliesDbContext familiesDb = CreateFamiliesDbContext();

        Caregiver caregiver = await AddCaregiverAsync(caregiversDb);
        Caregiver other = await AddCaregiverAsync(caregiversDb);

        Booking booking = await AddCompletedBookingAsync(
            familiesDb,
            caregiver.Id);
        CaregiverPayout payout = RecordPayout(booking, caregiver.Id, Utc(9));
        caregiversDb.Payouts.Add(payout);
        await caregiversDb.SaveChangesAsync();

        var handler = new GetCaregiverPayoutDetailQueryHandler(caregiversDb);

        var wrongOwner = await handler.Handle(
            new GetCaregiverPayoutDetailQuery(
                other.Id.Value,
                payout.Id.Value),
            default);

        Assert.True(wrongOwner.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.NotFound, wrongOwner.Error);

        var missing = await handler.Handle(
            new GetCaregiverPayoutDetailQuery(
                caregiver.Id.Value,
                Guid.CreateVersion7()),
            default);

        Assert.True(missing.IsFailure);
        Assert.Equal(CaregiverPayoutErrors.NotFound, missing.Error);
    }

    [Fact]
    public async Task PaymentPolicy_ShouldReturnDescriptionsAndHonestNulls()
    {
        var handler = new GetCaregiverPaymentPolicyQueryHandler(
            new FixedPolicyReader(
                new CaregiverPayoutPolicyRates(
                    72,
                    2,
                    "يُدفع بعد 72 ساعة من إتمام الحجز وسداده",
                    "Payout after 72 hours from booking completion and payment.")),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetCaregiverPaymentPolicyQuery(),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "يُدفع بعد 72 ساعة من إتمام الحجز وسداده",
            result.Value.DescriptionArabic);
        Assert.Equal(
            "Payout after 72 hours from booking completion and payment.",
            result.Value.DescriptionEnglish);
        Assert.True(result.Value.RequiresVerifiedBankAccount);
        Assert.Null(result.Value.MinimumPayoutAmount);
        Assert.Null(result.Value.ExpectedProcessingDays);
    }

    [Fact]
    public async Task PaymentPolicy_ShouldReturnNullDescriptions_WhenNoPolicyExists()
    {
        var handler = new GetCaregiverPaymentPolicyQueryHandler(
            new FixedPolicyReader(null),
            new FixedClock(Utc(20)));

        var result = await handler.Handle(
            new GetCaregiverPaymentPolicyQuery(),
            default);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.DescriptionArabic);
        Assert.Null(result.Value.DescriptionEnglish);
        Assert.True(result.Value.RequiresVerifiedBankAccount);
        Assert.Null(result.Value.MinimumPayoutAmount);
        Assert.Null(result.Value.ExpectedProcessingDays);
    }

    private static CaregiverPayout RecordPayout(
        Booking booking,
        CaregiverId caregiverId,
        DateTime recordedOnUtc,
        string transferReference = "BANK-2026-000123") =>
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
            transferReference,
            "Transfer advice 000123",
            "October weekly payout",
            UserId.New(),
            recordedOnUtc);

    private static CaregiverPayout RecordFailedPayout(
        Booking booking,
        CaregiverId caregiverId,
        DateTime recordedOnUtc,
        DateTime failedOnUtc)
    {
        CaregiverPayout payout = RecordPayout(
            booking,
            caregiverId,
            recordedOnUtc,
            transferReference: "BANK-000999");

        payout.MarkFailed(
            "Transfer declined by bank",
            UserId.New(),
            failedOnUtc);

        return payout;
    }

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

        if (status == PayoutAccountStatus.Verified)
        {
            account.Verify(UserId.New(), 1, Utc(2), "BankPortal", null);
        }

        dbContext.PayoutAccounts.Add(account);
        await dbContext.SaveChangesAsync();
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

    private static DateTime Utc(int day) =>
        new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
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
