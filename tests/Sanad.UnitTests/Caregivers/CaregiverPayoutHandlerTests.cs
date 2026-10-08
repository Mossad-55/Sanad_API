using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.PayoutAccounts;
using Sanad.Modules.Caregivers.Application.Payouts;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Families.Application;
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
        dbContext.PayoutAccounts.Add(
            CaregiverPayoutAccount.Create(
                caregiverId,
                "Mohamed Ahmed",
                "NBE",
                "v1.test.ciphertext",
                "0002"));

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

    private static DateTime Utc(int day) => new(2026, 10, day, 0, 0, 0, DateTimeKind.Utc);

    private sealed class FixedPolicyReader(
        CaregiverPayoutPolicyRates? rates) : ICaregiverPayoutPolicyReader
    {
        public Task<CaregiverPayoutPolicyRates?> GetEffectiveAsync(
            DateTime utcNow,
            CancellationToken cancellationToken) =>
            Task.FromResult(rates);

        public Task<IReadOnlyList<CaregiverPayoutPolicyHistoryItem>> GetHistoryAsync(
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CaregiverPayoutPolicyHistoryItem>>([]);
    }
}
