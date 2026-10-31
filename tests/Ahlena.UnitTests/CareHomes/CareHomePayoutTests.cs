using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Domain.Bookings;
using Ahlena.Modules.CareHomes.Domain.Facilities;
using Ahlena.Modules.CareHomes.Infrastructure.Persistence;
using Ahlena.Modules.Finance.Application;

namespace Ahlena.UnitTests.CareHomes;

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
    public void Payout_routes_require_payout_operational_admin()
    {
        var controller = typeof(AdminCareHomePayoutsController);
        Assert.Equal(AuthorizationPolicies.PayoutOperationalAdmin,
            Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("ledger", controller.GetMethod(nameof(AdminCareHomePayoutsController.Ledger))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("bookings/{bookingId:guid}/record", controller.GetMethod(nameof(AdminCareHomePayoutsController.Record))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("bookings/{bookingId:guid}/record-reversal", controller.GetMethod(nameof(AdminCareHomePayoutsController.RecordReversal))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public async Task Owner_settlement_view_scopes_to_owner_facility_and_includes_unpaid_and_recorded_rows()
    {
        await using var db = CreateDb();
        UserId ownerA = UserId.New();
        UserId ownerB = UserId.New();
        var facilityA = CareHomeFacility.CreateDraft(ownerA, Now);
        var facilityB = CareHomeFacility.CreateDraft(ownerB, Now);
        var unpaidA = CreateEligibleBooking(facilityA.Id, Now.AddDays(2));
        var recordedA = CreateEligibleBooking(facilityA.Id, Now.AddDays(3), transactionId: 2002);
        var foreignB = CreateEligibleBooking(facilityB.Id, Now.AddDays(4), transactionId: 3003);
        db.Facilities.AddRange(facilityA, facilityB);
        db.Bookings.AddRange(unpaidA, recordedA, foreignB);
        var payout = CareHomePayout.Record(recordedA.Id, facilityA.Id, 1000m, 1150m, 5m, 50m, 3,
            "BANK-REF-A", "receipt.pdf", "settlement", UserId.New(), Now.AddDays(3).AddHours(1));
        db.Payouts.Add(payout);
        await db.SaveChangesAsync();

        var result = await new GetOwnerCareHomePayoutLedgerHandler(db, new FixedChargeRules(new(10m, 7m, 4)))
            .Handle(new(ownerA, Now), default);

        Assert.True(result.IsSuccess);
        // Recorded transfer (checkout day 3) precedes the eligible unpaid balance (day 2).
        Assert.Equal(2, result.Value.Count);
        CareHomePayoutLedgerItem recorded = result.Value[0];
        Assert.Equal(recordedA.Id, recorded.BookingId);
        Assert.Equal(payout.Id, recorded.PayoutId);
        Assert.Equal(1000m, recorded.RemainingGross);
        Assert.Equal(5m, recorded.FeeRatePercentage);
        Assert.Equal(50m, recorded.FeeAmount);
        Assert.Equal(950m, recorded.NetPayable);
        Assert.Equal(1150m, recorded.RemainingCustomerAmount);
        Assert.Equal(0m, recorded.FacilityDebt);
        Assert.Equal("EGP", recorded.Currency);
        Assert.Equal("BANK-REF-A", recorded.TransferReference);
        Assert.Equal(Now.AddDays(3).AddHours(1), recorded.RecordedOnUtc);

        CareHomePayoutLedgerItem unpaid = result.Value[1];
        Assert.Equal(unpaidA.Id, unpaid.BookingId);
        Assert.Null(unpaid.PayoutId);
        Assert.Equal(1000m, unpaid.RemainingGross);
        Assert.Equal(10m, unpaid.FeeRatePercentage);
        Assert.Equal(100m, unpaid.FeeAmount);
        Assert.Equal(900m, unpaid.NetPayable);
        Assert.Equal(1150m, unpaid.RemainingCustomerAmount);
        Assert.Null(unpaid.TransferReference);
        Assert.Null(unpaid.RecordedOnUtc);

        // Facility and foreign-owner isolation.
        Assert.DoesNotContain(result.Value, x => x.BookingId == foreignB.Id);
        Assert.DoesNotContain(result.Value, x => x.FacilityId == facilityB.Id.Value);

        // Read-only: the handler tracks nothing and never creates a settlement/debt row.
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Single(db.Payouts);
        Assert.Empty(db.PayoutDebts);
    }

    [Fact]
    public async Task Owner_settlement_view_shows_debt_after_recorded_payout_without_a_finance_rule()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        var booking = CreateEligibleBooking(facility.Id, Now.AddDays(2));
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        var payout = CareHomePayout.Record(booking.Id, facility.Id, 1000m, 1150m, 5m, 50m, 3,
            "BANK-REF", "receipt.pdf", "settlement", UserId.New(), Now.AddDays(3));
        db.Payouts.Add(payout);
        db.PayoutDebts.Add(CareHomePayoutDebt.Record(payout.Id, booking.Id, 575m, 475m,
            "REFUND-1", "Partial refund", UserId.New(), Now.AddDays(4)));
        await db.SaveChangesAsync();

        // A recorded transfer must not require an effective Finance rule.
        var result = await new GetOwnerCareHomePayoutLedgerHandler(db, new FixedChargeRules(null))
            .Handle(new(owner, Now), default);

        Assert.True(result.IsSuccess);
        CareHomePayoutLedgerItem row = Assert.Single(result.Value);
        Assert.Equal(payout.Id, row.PayoutId);
        Assert.Equal(475m, row.FacilityDebt);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Owner_settlement_view_excludes_ineligible_rows_without_a_recorded_payout()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        db.Facilities.Add(facility);
        // Completed checkout with a still-pending refund: not an eligible unpaid balance.
        var pendingRefund = CreateCompletedBooking(facility.Id, refund: true, completeRefund: false);
        // Never completed a stay: the shared ledger query never loads it.
        var pendingStay = CreatePendingBooking(facility.Id);
        db.Bookings.AddRange(pendingRefund, pendingStay);
        await db.SaveChangesAsync();

        var result = await new GetOwnerCareHomePayoutLedgerHandler(db, new FixedChargeRules(new(10m, 7m, 4)))
            .Handle(new(owner, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Owner_settlement_view_excludes_zero_payable_non_payout_rows()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        var booking = CreateEligibleBooking(facility.Id, Now.AddDays(2));
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        // A 100% Finance fee makes the calculated netPayable zero; it must not be returned.
        var result = await new GetOwnerCareHomePayoutLedgerHandler(db, new FixedChargeRules(new(100m, 0m, 1)))
            .Handle(new(owner, Now), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Owner_settlement_view_returns_not_found_without_a_facility_and_empty_with_no_rows()
    {
        await using var db = CreateDb();

        var noFacility = await new GetOwnerCareHomePayoutLedgerHandler(db, new FixedChargeRules(new(10m, 7m, 4)))
            .Handle(new(UserId.New(), Now), default);
        Assert.False(noFacility.IsSuccess);
        Assert.Equal("CareHomes.Bookings.NotFound", noFacility.Error.Code);

        UserId owner = UserId.New();
        db.Facilities.Add(CareHomeFacility.CreateDraft(owner, Now));
        await db.SaveChangesAsync();

        var empty = await new GetOwnerCareHomePayoutLedgerHandler(db, new FixedChargeRules(new(10m, 7m, 4)))
            .Handle(new(owner, Now), default);
        Assert.True(empty.IsSuccess);
        Assert.Empty(empty.Value);
    }

    [Fact]
    public async Task Owner_settlement_view_fails_closed_without_an_effective_finance_rule()
    {
        await using var db = CreateDb();
        UserId owner = UserId.New();
        var facility = CareHomeFacility.CreateDraft(owner, Now);
        var booking = CreateEligibleBooking(facility.Id, Now.AddDays(2));
        db.Facilities.Add(facility);
        db.Bookings.Add(booking);
        await db.SaveChangesAsync();

        var result = await new GetOwnerCareHomePayoutLedgerHandler(db, new FixedChargeRules(null))
            .Handle(new(owner, Now), default);

        Assert.False(result.IsSuccess);
        Assert.Equal("CareHomes.Payout.FinanceConfigurationMissing", result.Error.Code);
        Assert.Empty(db.Payouts);
    }

    [Fact]
    public void Owner_settlement_query_has_no_caller_supplied_facility_scope()
    {
        Assert.Equal(2, typeof(GetOwnerCareHomePayoutLedgerQuery).GetProperties().Length);
        Assert.Null(typeof(GetOwnerCareHomePayoutLedgerQuery).GetProperty("FacilityId"));
    }

    private static CareHomeBooking CreatePendingBooking(CareHomeId facilityId) =>
        CareHomeBooking.Create(facilityId, UserId.New(), FamilyId.New(), ElderlyId.New(),
            Guid.NewGuid(), new DateOnly(2026, 10, 10), 1000m, 100m, 50m, 2, "عربي", "English",
            75, null, "Contact", null, null, null, Now);

    private static CareHomeBooking CreateEligibleBooking(CareHomeId facilityId,
        DateTime checkOutOnUtc, long transactionId = 1001)
    {
        var booking = CareHomeBooking.Create(facilityId, UserId.New(), FamilyId.New(), ElderlyId.New(),
            Guid.NewGuid(), new DateOnly(2026, 10, 10), 1000m, 100m, 50m, 2, "عربي", "English",
            75, null, "Contact", null, null, null, Now);
        var actor = UserId.New();
        booking.MarkPaid(transactionId, Now.AddMinutes(1));
        booking.Accept(Now.AddMinutes(2));
        booking.AssignPhysicalResource(Guid.NewGuid(), null, Now.AddMinutes(3));
        booking.RecordCheckIn(actor, Now.AddDays(1));
        booking.RecordCheckOut(actor, checkOutOnUtc);
        return booking;
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
