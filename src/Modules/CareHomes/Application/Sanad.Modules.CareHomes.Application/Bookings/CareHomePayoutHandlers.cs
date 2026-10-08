using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.Finance.Application;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record CareHomePayoutLedgerItem(
    Guid BookingId, Guid FacilityId, DateOnly StartDate, DateOnly EndDate,
    decimal RemainingCustomerAmount, decimal RemainingGross, decimal FeeRatePercentage,
    decimal FeeAmount, decimal NetPayable, Guid? PayoutId, decimal FacilityDebt,
    string Currency, string? TransferReference, DateTime? RecordedOnUtc);

public sealed record GetCareHomePayoutLedgerQuery(Guid? FacilityId = null, DateTime UtcNow = default)
    : IQuery<IReadOnlyList<CareHomePayoutLedgerItem>>;

public sealed record RecordCareHomePayoutCommand(
    UserId Actor, Guid BookingId, string TransferReference, string Evidence,
    string Reason, DateTime UtcNow) : ICommand<CareHomePayoutLedgerItem>;

public sealed record RecordCareHomePayoutReversalCommand(
    UserId Actor, Guid BookingId, decimal CustomerRefundAmount, string Reference,
    string Reason, DateTime UtcNow) : ICommand<CareHomePayoutLedgerItem>;

public static class CareHomePayoutErrors
{
    public static readonly Error NotFound = new("CareHomes.Payout.NotFound", "Care-home payable was not found.");
    public static readonly Error InvalidState = new("CareHomes.Payout.InvalidState", "This stay or refund is not eligible for payout or reversal recording.");
    public static readonly Error FinanceConfigurationMissing = new("CareHomes.Payout.FinanceConfigurationMissing", "An effective Finance fee configuration is required before a payout can be recorded.");
    public static readonly Error Conflict = new("CareHomes.Payout.Conflict", "A payout or ledger update already exists for this stay.");
}

public static class CareHomePayoutCalculation
{
    public static bool TryGetRemainingAmounts(CareHomeBooking booking, out decimal gross, out decimal customerAmount)
    {
        gross = 0m;
        customerAmount = 0m;
        if (booking.ActualCheckOutOnUtc is null
            || booking.Status is not (CareHomeBookingStatus.Accepted or CareHomeBookingStatus.Refunded)
            || booking.PaymentStatus is not (CareHomeBookingPaymentStatus.Paid or CareHomeBookingPaymentStatus.Refunded)
            || booking.RefundStatus is not (CareHomeRefundStatus.None or CareHomeRefundStatus.Completed or CareHomeRefundStatus.ManuallyCompleted))
            return false;

        decimal refunded = 0m;
        if (booking.RefundStatus is CareHomeRefundStatus.Completed or CareHomeRefundStatus.ManuallyCompleted)
        {
            if (booking.RefundAmount is null || booking.TotalAmount <= 0m
                || booking.RefundAmount.Value < 0m || booking.RefundAmount.Value > booking.TotalAmount)
                return false;
            refunded = booking.RefundAmount.Value;
        }

        decimal refundedBase = booking.TotalAmount <= 0m
            ? 0m
            : decimal.Round(booking.BaseAmount * refunded / booking.TotalAmount, 2, MidpointRounding.ToEven);
        gross = decimal.Round(Math.Max(0m, booking.BaseAmount - refundedBase), 2, MidpointRounding.ToEven);
        customerAmount = decimal.Round(Math.Max(0m, booking.TotalAmount - refunded), 2, MidpointRounding.ToEven);
        return gross > 0m && customerAmount > 0m;
    }
}

public sealed class GetCareHomePayoutLedgerHandler(
    ICareHomesDbContext db, IPlatformChargeRuleReader chargeRules)
    : IQueryHandler<GetCareHomePayoutLedgerQuery, IReadOnlyList<CareHomePayoutLedgerItem>>
{
    public async Task<Result<IReadOnlyList<CareHomePayoutLedgerItem>>> Handle(GetCareHomePayoutLedgerQuery request, CancellationToken ct)
    {
        var bookings = await db.Bookings.AsNoTracking()
            .Where(x => x.ActualCheckOutOnUtc != null
                && (x.PaymentStatus == CareHomeBookingPaymentStatus.Paid || x.PaymentStatus == CareHomeBookingPaymentStatus.Refunded)
                && (request.FacilityId == null || x.FacilityId.Value == request.FacilityId))
            .OrderByDescending(x => x.ActualCheckOutOnUtc).ToListAsync(ct);
        var payouts = await db.Payouts.AsNoTracking().ToDictionaryAsync(x => x.BookingId, ct);
        var debts = await db.PayoutDebts.AsNoTracking().GroupBy(x => x.BookingId)
            .Select(x => new { BookingId = x.Key, Amount = x.Sum(d => d.Amount) })
            .ToDictionaryAsync(x => x.BookingId, x => x.Amount, ct);

        bool hasUnsettledPayables = bookings.Any(x => !payouts.ContainsKey(x.Id)
            && CareHomePayoutCalculation.TryGetRemainingAmounts(x, out _, out _));
        PlatformChargeRuleRates? rates = hasUnsettledPayables
            ? await chargeRules.GetEffectiveAsync(request.UtcNow, ct)
            : null;
        if (hasUnsettledPayables && rates is null)
            return Result<IReadOnlyList<CareHomePayoutLedgerItem>>.Failure(CareHomePayoutErrors.FinanceConfigurationMissing);

        var rows = new List<CareHomePayoutLedgerItem>(bookings.Count);
        foreach (CareHomeBooking booking in bookings)
        {
            bool eligible = CareHomePayoutCalculation.TryGetRemainingAmounts(booking, out decimal gross, out decimal remainingCustomer);
            CareHomePayout? payout = payouts.GetValueOrDefault(booking.Id);
            decimal rate = payout?.FeeRatePercentage ?? rates?.PlatformFeeRatePercentage ?? 0m;
            decimal fee = payout?.FeeAmount ?? (eligible
                ? PlatformChargeCalculator.Calculate(gross, rate, 0m).PlatformFeeAmount
                : 0m);
            rows.Add(new(booking.Id, booking.FacilityId.Value, booking.StartDate, booking.EndDate,
                payout?.RemainingCustomerAmount ?? (eligible ? remainingCustomer : 0m),
                payout?.GrossAmount ?? (eligible ? gross : 0m), rate, fee,
                payout?.NetAmount ?? (eligible ? Math.Round(gross - fee, 2, MidpointRounding.ToEven) : 0m),
                payout?.Id, debts.GetValueOrDefault(booking.Id), payout?.Currency ?? "EGP",
                payout?.TransferReference, payout?.RecordedOnUtc));
        }
        return Result<IReadOnlyList<CareHomePayoutLedgerItem>>.Success(rows);
    }
}

public sealed class RecordCareHomePayoutHandler(
    ICareHomesDbContext db, IPlatformChargeRuleReader chargeRules)
    : ICommandHandler<RecordCareHomePayoutCommand, CareHomePayoutLedgerItem>
{
    public async Task<Result<CareHomePayoutLedgerItem>> Handle(RecordCareHomePayoutCommand request, CancellationToken ct)
    {
        if (request.UtcNow.Kind != DateTimeKind.Utc) return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);
        var booking = await db.Bookings.SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (booking is null) return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.NotFound);
        if (await db.Payouts.AnyAsync(x => x.BookingId == booking.Id, ct))
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.Conflict);
        if (!CareHomePayoutCalculation.TryGetRemainingAmounts(booking, out decimal gross, out decimal remainingCustomer))
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);

        PlatformChargeRuleRates? rates = await chargeRules.GetEffectiveAsync(request.UtcNow, ct);
        if (rates is null) return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.FinanceConfigurationMissing);
        PlatformChargeQuote quote;
        try { quote = PlatformChargeCalculator.Calculate(gross, rates.PlatformFeeRatePercentage, 0m); }
        catch (DomainException) { return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState); }
        if (quote.PlatformFeeAmount >= gross)
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);

        try
        {
            var payout = CareHomePayout.Record(booking.Id, booking.FacilityId, gross,
                remainingCustomer, quote.PlatformFeeRatePercentage, quote.PlatformFeeAmount,
                rates.Version, request.TransferReference, request.Evidence,
                request.Reason, request.Actor, request.UtcNow);
            db.Payouts.Add(payout);
            await db.SaveChangesAsync(ct);
            return Result<CareHomePayoutLedgerItem>.Success(new(booking.Id, booking.FacilityId.Value,
                booking.StartDate, booking.EndDate, payout.RemainingCustomerAmount,
                payout.GrossAmount, payout.FeeRatePercentage, payout.FeeAmount,
                payout.NetAmount, payout.Id, 0m, payout.Currency,
                payout.TransferReference, payout.RecordedOnUtc));
        }
        catch (DbUpdateException)
        {
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.Conflict);
        }
        catch (ArgumentException)
        {
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);
        }
    }
}

public sealed class RecordCareHomePayoutReversalHandler(ICareHomesDbContext db)
    : ICommandHandler<RecordCareHomePayoutReversalCommand, CareHomePayoutLedgerItem>
{
    public async Task<Result<CareHomePayoutLedgerItem>> Handle(RecordCareHomePayoutReversalCommand request, CancellationToken ct)
    {
        if (request.UtcNow.Kind != DateTimeKind.Utc || request.CustomerRefundAmount <= 0m)
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);
        CareHomePayout? payout = await db.Payouts.SingleOrDefaultAsync(x => x.BookingId == request.BookingId, ct);
        CareHomeBooking? booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (payout is null || booking is null) return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.NotFound);
        if (await db.PayoutDebts.AnyAsync(x => x.PayoutId == payout.Id && x.Reference == request.Reference.Trim(), ct))
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.Conflict);

        var existing = await db.PayoutDebts.Where(x => x.PayoutId == payout.Id).ToListAsync(ct);
        decimal cumulativeCustomerRefund = existing.Sum(x => x.CustomerRefundAmount) + request.CustomerRefundAmount;
        if (cumulativeCustomerRefund > payout.RemainingCustomerAmount)
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);
        decimal cumulativeDebt = decimal.Round(payout.NetAmount * cumulativeCustomerRefund / payout.RemainingCustomerAmount, 2, MidpointRounding.ToEven);
        decimal debtDelta = decimal.Round(cumulativeDebt - existing.Sum(x => x.Amount), 2, MidpointRounding.ToEven);
        if (debtDelta <= 0m) return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);

        try
        {
            var debt = CareHomePayoutDebt.Record(payout.Id, payout.BookingId,
                request.CustomerRefundAmount, debtDelta, request.Reference,
                request.Reason, request.Actor, request.UtcNow);
            payout.RecordDebtEntry();
            db.PayoutDebts.Add(debt);
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.Conflict);
        }
        catch (ArgumentException)
        {
            return Result<CareHomePayoutLedgerItem>.Failure(CareHomePayoutErrors.InvalidState);
        }
        decimal debtTotal = existing.Sum(x => x.Amount) + debtDelta;
        return Result<CareHomePayoutLedgerItem>.Success(new(payout.BookingId,
            payout.FacilityId.Value, booking.StartDate, booking.EndDate,
            payout.RemainingCustomerAmount, payout.GrossAmount, payout.FeeRatePercentage,
            payout.FeeAmount, payout.NetAmount, payout.Id, debtTotal, payout.Currency,
            payout.TransferReference, payout.RecordedOnUtc));
    }
}