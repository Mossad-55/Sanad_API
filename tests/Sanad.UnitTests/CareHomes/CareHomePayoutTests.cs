using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Finance.Application;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomePayoutTests
{
    private static readonly DateTime Now = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Payout_eligibility_requires_completed_stay_and_only_counts_completed_refunds()
    {
        var booking = CreateCompletedBooking();
        Assert.True(CareHomePayoutCalculation.TryGetRemainingAmounts(booking, out decimal gross, out decimal customer));
        Assert.Equal(1000m, gross);
        Assert.Equal(1150m, customer);

        var pendingRefund = CreateCompletedBooking(refund: true, completeRefund: false);
        Assert.False(CareHomePayoutCalculation.TryGetRemainingAmounts(pendingRefund, out _, out _));

        var partialRefund = CreateCompletedBooking(refund: true, completeRefund: true);
        Assert.True(CareHomePayoutCalculation.TryGetRemainingAmounts(partialRefund, out gross, out customer));
        Assert.Equal(500m, gross);
        Assert.Equal(575m, customer);

        var fullRefund = CreateCompletedBooking(fullRefund: true);
        Assert.False(CareHomePayoutCalculation.TryGetRemainingAmounts(fullRefund, out _, out _));
    }

    [Fact]
    public async Task Admin_records_one_payout_using_effective_finance_rule_and_remaining_payable()
    {
        await using var db = CreateDb();
        var facility = CareHomeFacility.CreateDraft(UserId.New(), Now);
        var booking = CreateCompletedBooking(facility.Id, refund: true, completeRefund: true);
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var handler = new RecordCareHomePayoutHandler(db, new FixedChargeRules(new(10m, 7m, 4)));
        var result = await handler.Handle(new(UserId.New(), booking.Id, "BANK-REF-1", "receipt.pdf", "Monthly settlement", Now), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(500m, result.Value.RemainingGross);
        Assert.Equal(50m, result.Value.FeeAmount);
        Assert.Equal(450m, result.Value.NetPayable);
        Assert.Equal(4, (await db.Payouts.SingleAsync()).ChargeRuleVersion);

        var duplicate = await handler.Handle(new(UserId.New(), booking.Id, "BANK-REF-2", "receipt2.pdf", "Duplicate", Now), default);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("CareHomes.Payout.Conflict", duplicate.Error.Code);
        Assert.Single(db.Payouts);
    }

    [Fact]
    public async Task Post_payout_refunds_create_proportional_net_debt_and_reference_is_idempotency_key()
    {
        await using var db = CreateDb();
        var facility = CareHomeFacility.CreateDraft(UserId.New(), Now);
        var booking = CreateCompletedBooking(facility.Id);
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();
        var payout = CareHomePayout.Record(booking.Id, facility.Id, 1000m, 1150m,
            5m, 50m, 3, "BANK-REF", "receipt.pdf", "settlement", UserId.New(), Now);
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var handler = new RecordCareHomePayoutReversalHandler(db);
        var first = await handler.Handle(new(UserId.New(), booking.Id, 575m, "REFUND-1", "Partial refund", Now.AddMinutes(1)), default);
        var duplicate = await handler.Handle(new(UserId.New(), booking.Id, 575m, "REFUND-1", "Duplicate callback", Now.AddMinutes(2)), default);
        var second = await handler.Handle(new(UserId.New(), booking.Id, 575m, "REFUND-2", "Remaining refund", Now.AddMinutes(3)), default);

        Assert.True(first.IsSuccess);
        Assert.Equal(475m, first.Value.FacilityDebt);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal("CareHomes.Payout.Conflict", duplicate.Error.Code);
        Assert.True(second.IsSuccess);
        Assert.Equal(950m, second.Value.FacilityDebt);
        Assert.Equal(new[] { 475m, 475m }, await db.PayoutDebts.OrderBy(x => x.RecordedOnUtc).Select(x => x.Amount).ToArrayAsync());
    }

    [Fact]
    public async Task Payout_fails_closed_without_effective_finance_rule()
    {
        await using var db = CreateDb();
        var facility = CareHomeFacility.CreateDraft(UserId.New(), Now);
        var booking = CreateCompletedBooking(facility.Id);
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await new RecordCareHomePayoutHandler(db, new FixedChargeRules(null))
            .Handle(new(UserId.New(), booking.Id, "BANK-REF", "proof", "settlement", Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Payout.FinanceConfigurationMissing", result.Error.Code);
        Assert.Empty(db.Payouts);
    }

    [Fact]
    public void Payout_routes_require_care_homes_operational_admin()
    {
        var controller = typeof(AdminCareHomePayoutsController);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin,
            Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("ledger", controller.GetMethod(nameof(AdminCareHomePayoutsController.Ledger))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("bookings/{bookingId:guid}/record", controller.GetMethod(nameof(AdminCareHomePayoutsController.Record))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("bookings/{bookingId:guid}/record-reversal", controller.GetMethod(nameof(AdminCareHomePayoutsController.RecordReversal))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    private static CareHomeBooking CreateCompletedBooking(CareHomeId? facilityId = null,
        bool refund = false, bool completeRefund = false, bool fullRefund = false)
    {
        var booking = CareHomeBooking.Create(facilityId ?? CareHomeId.New(), UserId.New(), FamilyId.New(),
            ElderlyId.New(), Guid.NewGuid(), new DateOnly(2026, 10, 10), 1000m, 100m, 50m,
            2, "عربي", "English", 75, null, "Contact", null, null, null, Now);
        var actor = UserId.New();
        booking.MarkPaid(1001, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        booking.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(3));
        if (fullRefund)
        {
            // Facility cancellation before check-in refunds the full amount and ends the stay.
            booking.CancelByFacility("Facility cancellation", Now.AddDays(1));
            booking.MarkRefundManuallyCompleted("EXT-REFUND", "External refund confirmed", actor, Now.AddDays(1).AddMinutes(1));
            return booking;
        }
        booking.RecordCheckIn(actor, Now.AddDays(1));
        if (refund)
        {
            booking.CancelByFamily(Now.AddDays(1).AddMinutes(1));
            if (completeRefund)
                booking.MarkRefundManuallyCompleted("EXT-REFUND", "External refund confirmed", actor, Now.AddDays(1).AddMinutes(2));
        }
        booking.RecordCheckOut(actor, Now.AddDays(2));
        return booking;
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class FixedChargeRules(PlatformChargeRuleRates? rates) : IPlatformChargeRuleReader
    {
        public Task<PlatformChargeRuleRates?> GetEffectiveAsync(DateTime utcNow, CancellationToken cancellationToken) => Task.FromResult(rates);
        public Task<IReadOnlyList<PlatformChargeRuleHistoryItem>> GetHistoryAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<PlatformChargeRuleHistoryItem>>([]);
    }
}
