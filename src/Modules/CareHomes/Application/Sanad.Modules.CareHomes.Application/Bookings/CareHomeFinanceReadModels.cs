using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record CareHomeReceipt(
    Guid BookingId, Guid FacilityId, string Currency, string MerchantReference,
    long? PaymentTransactionId, DateTime? PaymentCompletedOnUtc,
    decimal BaseAmount, decimal PlatformFeeAmount, decimal TaxAmount, decimal OriginalTotal,
    CareHomeBookingPaymentStatus PaymentStatus, decimal CompletedRefundAmount,
    string? RefundReference, DateTime? RefundCompletedOnUtc, decimal NetCollected);

public sealed record GetOwnerCareHomeReceiptQuery(UserId Actor, Guid BookingId) : IQuery<CareHomeReceipt>;
public sealed record GetAdminCareHomeReceiptQuery(Guid BookingId) : IQuery<CareHomeReceipt>;

public sealed record CareHomeRevenueTransaction(
    Guid BookingId, Guid FacilityId, DateOnly StayStartDate, DateOnly StayEndDate,
    DateTime? PaymentCompletedOnUtc, long? PaymentTransactionId,
    decimal AmountCollectedInPeriod, DateTime? RefundCompletedOnUtc,
    string? RefundReference, decimal RefundAmountInPeriod, decimal NetMovement,
    string Currency, CareHomeBookingStatus BookingStatus,
    CareHomeBookingPaymentStatus PaymentStatus, CareHomeRefundStatus RefundStatus);

public sealed record CareHomeRevenueTotals(
    decimal BaseAmount, decimal PlatformFeeAmount, decimal TaxAmount,
    decimal GrossCollected, decimal CompletedRefunds, decimal NetCollected,
    int PaymentCount, int RefundCount);

public sealed record CareHomeRevenueReport(
    DateOnly? From, DateOnly? To, CareHomeRevenueTotals Totals,
    IReadOnlyList<CareHomeRevenueTransaction> Transactions);

public sealed record CareHomeDashboard(
    DateOnly CairoDate, int OccupiedResources, int TotalResources, decimal OccupancyPercentage,
    int PendingPaymentCount, int AwaitingDecisionCount, CareHomeRevenueTotals PeriodRevenue);

public sealed record GetOwnerCareHomeDashboardQuery(UserId Actor, DateOnly? From, DateOnly? To, DateTime UtcNow)
    : IQuery<CareHomeDashboard>;
public sealed record GetAdminCareHomeDashboardQuery(Guid? FacilityId, DateOnly? From, DateOnly? To, DateTime UtcNow)
    : IQuery<CareHomeDashboard>;
public sealed record GetOwnerCareHomeRevenueQuery(UserId Actor, DateOnly? From, DateOnly? To)
    : IQuery<CareHomeRevenueReport>;
public sealed record GetAdminCareHomeRevenueQuery(Guid? FacilityId, DateOnly? From, DateOnly? To)
    : IQuery<CareHomeRevenueReport>;

public static class CareHomeRevenueProjection
{
    public static readonly Error NotFound = new("CareHomes.Bookings.NotFound", "Booking or facility was not found.");
    public static readonly Error InvalidRange = new("CareHomes.Revenue.InvalidRange", "The Cairo-local date range is invalid.");

    public static TimeZoneInfo CairoZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time"); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo"); }
    }

    public static (DateTime? FromUtc, DateTime? UntilUtc) ToUtcRange(DateOnly? from, DateOnly? to)
    {
        if (from is not null && to is not null && to < from) throw new ArgumentException("Invalid date range.");
        TimeZoneInfo zone = CairoZone();
        DateTime? start = from is null ? null : TimeZoneInfo.ConvertTimeToUtc(from.Value.ToDateTime(TimeOnly.MinValue), zone);
        DateTime? until = to is null ? null : TimeZoneInfo.ConvertTimeToUtc(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), zone);
        return (start, until);
    }

    public static CareHomeReceipt MapReceipt(CareHomeBooking booking)
    {
        decimal refund = booking.RefundStatus is CareHomeRefundStatus.Completed or CareHomeRefundStatus.ManuallyCompleted
            ? booking.RefundAmount ?? 0m : 0m;
        return new(booking.Id, booking.FacilityId.Value, "EGP", booking.MerchantReference,
            booking.PaymobTransactionId, booking.PaymentCompletedOnUtc,
            booking.BaseAmount, booking.PlatformFeeAmount, booking.TaxAmount, booking.TotalAmount,
            booking.PaymentStatus, refund,
            refund > 0m ? booking.RefundReference : null,
            refund > 0m ? booking.RefundCompletedOnUtc : null,
            Math.Max(0m, booking.TotalAmount - refund));
    }

    public static async Task<Result<CareHomeRevenueReport>> Revenue(
        ICareHomesDbContext db, Guid? facilityId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        (DateTime? start, DateTime? until) range;
        try { range = ToUtcRange(from, to); }
        catch (ArgumentException) { return Result<CareHomeRevenueReport>.Failure(InvalidRange); }

        var bookings = await db.Bookings.AsNoTracking()
            .Where(x => facilityId == null || x.FacilityId.Value == facilityId)
            .Where(x => (x.PaymentStatus == CareHomeBookingPaymentStatus.Paid
                         || x.PaymentStatus == CareHomeBookingPaymentStatus.Refunded
                         || x.RefundStatus == CareHomeRefundStatus.Completed
                         || x.RefundStatus == CareHomeRefundStatus.ManuallyCompleted)
                && ((range.start == null && range.until == null)
                    || (x.PaymentCompletedOnUtc != null && (range.start == null || x.PaymentCompletedOnUtc >= range.start)
                        && (range.until == null || x.PaymentCompletedOnUtc < range.until))
                    || ((x.RefundStatus == CareHomeRefundStatus.Completed || x.RefundStatus == CareHomeRefundStatus.ManuallyCompleted)
                        && x.RefundCompletedOnUtc != null
                        && (range.start == null || x.RefundCompletedOnUtc >= range.start)
                        && (range.until == null || x.RefundCompletedOnUtc < range.until))))
            .OrderBy(x => x.PaymentCompletedOnUtc).ThenBy(x => x.CreatedOnUtc).ToListAsync(ct);

        var rows = new List<CareHomeRevenueTransaction>(bookings.Count);
        decimal baseCollected = 0m, feeCollected = 0m, taxCollected = 0m, collected = 0m, refunded = 0m;
        int payments = 0, refunds = 0;
        foreach (CareHomeBooking booking in bookings)
        {
            bool paymentInPeriod = booking.PaymentStatus is CareHomeBookingPaymentStatus.Paid or CareHomeBookingPaymentStatus.Refunded
                && (range.start is null && range.until is null
                    || booking.PaymentCompletedOnUtc is DateTime paidAt && InRange(paidAt, range.start, range.until));
            bool refundInPeriod = booking.RefundStatus is CareHomeRefundStatus.Completed or CareHomeRefundStatus.ManuallyCompleted
                && booking.RefundCompletedOnUtc is DateTime refundAt
                && InRange(refundAt, range.start, range.until);
            decimal paymentAmount = paymentInPeriod ? booking.TotalAmount : 0m;
            decimal refundAmount = refundInPeriod ? booking.RefundAmount ?? 0m : 0m;
            if (paymentInPeriod)
            {
                baseCollected += booking.BaseAmount;
                feeCollected += booking.PlatformFeeAmount;
                taxCollected += booking.TaxAmount;
                collected += paymentAmount;
                payments++;
            }
            if (refundInPeriod) { refunded += refundAmount; refunds++; }
            rows.Add(new(booking.Id, booking.FacilityId.Value, booking.StartDate, booking.EndDate,
                booking.PaymentCompletedOnUtc, booking.PaymobTransactionId, paymentAmount,
                booking.RefundCompletedOnUtc, refundInPeriod ? booking.RefundReference : null,
                refundAmount, paymentAmount - refundAmount, "EGP", booking.Status,
                booking.PaymentStatus, booking.RefundStatus));
        }
        var totals = new CareHomeRevenueTotals(baseCollected, feeCollected, taxCollected,
            collected, refunded, collected - refunded, payments, refunds);
        return Result<CareHomeRevenueReport>.Success(new(from, to, totals, rows));
    }

    private static bool InRange(DateTime value, DateTime? start, DateTime? until) =>
        (start is null || value >= start) && (until is null || value < until);
}

public sealed class GetOwnerCareHomeReceiptHandler(ICareHomesDbContext db)
    : IQueryHandler<GetOwnerCareHomeReceiptQuery, CareHomeReceipt>
{
    public async Task<Result<CareHomeReceipt>> Handle(GetOwnerCareHomeReceiptQuery q, CancellationToken ct)
    {
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == q.Actor, ct);
        if (facility is null) return Result<CareHomeReceipt>.Failure(CareHomeRevenueProjection.NotFound);
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == q.BookingId && x.FacilityId == facility.Id, ct);
        return booking is null || booking.PaymentStatus is not (CareHomeBookingPaymentStatus.Paid or CareHomeBookingPaymentStatus.Refunded)
            ? Result<CareHomeReceipt>.Failure(CareHomeRevenueProjection.NotFound)
            : Result<CareHomeReceipt>.Success(CareHomeRevenueProjection.MapReceipt(booking));
    }
}

public sealed class GetAdminCareHomeReceiptHandler(ICareHomesDbContext db)
    : IQueryHandler<GetAdminCareHomeReceiptQuery, CareHomeReceipt>
{
    public async Task<Result<CareHomeReceipt>> Handle(GetAdminCareHomeReceiptQuery q, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == q.BookingId, ct);
        return booking is null || booking.PaymentStatus is not (CareHomeBookingPaymentStatus.Paid or CareHomeBookingPaymentStatus.Refunded)
            ? Result<CareHomeReceipt>.Failure(CareHomeRevenueProjection.NotFound)
            : Result<CareHomeReceipt>.Success(CareHomeRevenueProjection.MapReceipt(booking));
    }
}

public sealed class GetOwnerCareHomeRevenueHandler(ICareHomesDbContext db)
    : IQueryHandler<GetOwnerCareHomeRevenueQuery, CareHomeRevenueReport>
{
    public async Task<Result<CareHomeRevenueReport>> Handle(GetOwnerCareHomeRevenueQuery q, CancellationToken ct)
    {
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == q.Actor, ct);
        return facility is null ? Result<CareHomeRevenueReport>.Failure(CareHomeRevenueProjection.NotFound)
            : await CareHomeRevenueProjection.Revenue(db, facility.Id.Value, q.From, q.To, ct);
    }
}

public sealed class GetAdminCareHomeRevenueHandler(ICareHomesDbContext db)
    : IQueryHandler<GetAdminCareHomeRevenueQuery, CareHomeRevenueReport>
{
    public Task<Result<CareHomeRevenueReport>> Handle(GetAdminCareHomeRevenueQuery q, CancellationToken ct) =>
        CareHomeRevenueProjection.Revenue(db, q.FacilityId, q.From, q.To, ct);
}

public sealed class GetOwnerCareHomeDashboardHandler(ICareHomesDbContext db, ICareHomeOccupancyProvider occupancy)
    : IQueryHandler<GetOwnerCareHomeDashboardQuery, CareHomeDashboard>
{
    public async Task<Result<CareHomeDashboard>> Handle(GetOwnerCareHomeDashboardQuery q, CancellationToken ct)
    {
        var facility = await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == q.Actor, ct);
        return facility is null ? Result<CareHomeDashboard>.Failure(CareHomeRevenueProjection.NotFound)
            : await CareHomeDashboardProjection.Get(db, occupancy, facility.Id.Value, q.From, q.To, q.UtcNow, ct);
    }
}

public sealed class GetAdminCareHomeDashboardHandler(ICareHomesDbContext db, ICareHomeOccupancyProvider occupancy)
    : IQueryHandler<GetAdminCareHomeDashboardQuery, CareHomeDashboard>
{
    public Task<Result<CareHomeDashboard>> Handle(GetAdminCareHomeDashboardQuery q, CancellationToken ct) =>
        CareHomeDashboardProjection.Get(db, occupancy, q.FacilityId, q.From, q.To, q.UtcNow, ct);
}

internal static class CareHomeDashboardProjection
{
    public static async Task<Result<CareHomeDashboard>> Get(ICareHomesDbContext db,
        ICareHomeOccupancyProvider occupancy, Guid? facilityId, DateOnly? from, DateOnly? to,
        DateTime utcNow, CancellationToken ct)
    {
        if (utcNow.Kind != DateTimeKind.Utc) return Result<CareHomeDashboard>.Failure(CareHomeRevenueProjection.InvalidRange);
        var revenue = await CareHomeRevenueProjection.Revenue(db, facilityId, from, to, ct);
        if (revenue.IsFailure) return Result<CareHomeDashboard>.Failure(revenue.Error);
        var facilities = await db.Facilities.AsNoTracking()
            .Where(x => facilityId == null || x.Id.Value == facilityId).Select(x => x.Id).ToListAsync(ct);
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utcNow, CareHomeRevenueProjection.CairoZone()));
        int occupied = 0, total = 0;
        foreach (CareHomeId id in facilities)
        {
            var rooms = await db.Rooms.AsNoTracking().Where(x => x.FacilityId == id && !x.IsArchived).ToListAsync(ct);
            var roomIds = rooms.Select(x => x.Id).ToArray();
            var beds = await db.Beds.AsNoTracking().Where(x => roomIds.Contains(x.RoomId) && !x.IsArchived).ToListAsync(ct);
            var typeIds = rooms.Select(x => x.RoomTypeId).Distinct().ToArray();
            var allocationModes = await db.RoomTypes.AsNoTracking().Where(x => typeIds.Contains(x.Id))
                .ToDictionaryAsync(x => x.Id, x => x.AllocationMode, ct);
            var holds = await occupancy.GetActiveAsync(id, ct);
            var active = holds.Where(x => x.Overlaps(today, today.AddDays(1))).ToArray();
            var roomSet = rooms.Select(x => x.Id).ToHashSet();
            var bedSet = beds.Select(x => x.Id).ToHashSet();
            var sharedRoomIds = rooms.Where(x => allocationModes.GetValueOrDefault(x.RoomTypeId) == CareHomeAllocationMode.Shared)
                .Select(x => x.Id).ToHashSet();
            var physicalRoomIds = roomSet.Except(sharedRoomIds).ToHashSet();
            var sharedBedIds = beds.Where(x => sharedRoomIds.Contains(x.RoomId)).Select(x => x.Id).ToHashSet();
            total += physicalRoomIds.Count + sharedBedIds.Count;
            occupied += active.Count(x => x.ResourceKind == CareHomeResourceKind.Room && physicalRoomIds.Contains(x.ResourceId)
                || x.ResourceKind == CareHomeResourceKind.Bed && sharedBedIds.Contains(x.ResourceId));
            occupied += active.Where(x => x.ResourceKind == CareHomeResourceKind.RoomType).Sum(x => x.Quantity);
        }
        var bookingQuery = db.Bookings.AsNoTracking().Where(x => facilityId == null || x.FacilityId.Value == facilityId);
        int pendingPayment = await bookingQuery.CountAsync(x => x.Status == CareHomeBookingStatus.PendingPayment
            && x.CheckoutHoldUntilUtc > utcNow, ct);
        int awaitingDecision = await bookingQuery.CountAsync(x => x.Status == CareHomeBookingStatus.PaidAwaitingDecision
            && x.DecisionHoldUntilUtc > utcNow, ct);
        decimal occupancyPercentage = total == 0 ? 0m : decimal.Round(occupied * 100m / total, 2, MidpointRounding.ToEven);
        return Result<CareHomeDashboard>.Success(new(today, occupied, total, occupancyPercentage, pendingPayment,
            awaitingDecision, revenue.Value.Totals));
    }
}

public sealed record CareHomeInternalBookingNoteItem(Guid Id, Guid BookingId, Guid AuthorUserId,
    string Text, DateTime CreatedOnUtc);
public sealed record GetOwnerCareHomeInternalBookingNotesQuery(UserId Actor, Guid BookingId)
    : IQuery<IReadOnlyList<CareHomeInternalBookingNoteItem>>;
public sealed record GetAdminCareHomeInternalBookingNotesQuery(Guid BookingId)
    : IQuery<IReadOnlyList<CareHomeInternalBookingNoteItem>>;
public sealed record AddOwnerCareHomeInternalBookingNoteCommand(UserId Actor, Guid BookingId, string Text, DateTime UtcNow)
    : ICommand<CareHomeInternalBookingNoteItem>;
public sealed record AddAdminCareHomeInternalBookingNoteCommand(UserId Actor, Guid BookingId, string Text, DateTime UtcNow)
    : ICommand<CareHomeInternalBookingNoteItem>;

internal static class CareHomeInternalNoteHandlers
{
    public static async Task<Result<IReadOnlyList<CareHomeInternalBookingNoteItem>>> List(
        ICareHomesDbContext db, Guid bookingId, UserId? ownerUserId, CancellationToken ct)
    {
        CareHomeFacility? ownerFacility = ownerUserId is UserId owner
            ? await db.Facilities.AsNoTracking().SingleOrDefaultAsync(x => x.OwnerUserId == owner, ct)
            : null;
        if (ownerUserId is not null && ownerFacility is null)
            return Result<IReadOnlyList<CareHomeInternalBookingNoteItem>>.Failure(CareHomeRevenueProjection.NotFound);
        var bookingQuery = db.Bookings.AsNoTracking().Where(x => x.Id == bookingId);
        if (ownerFacility is not null) bookingQuery = bookingQuery.Where(x => x.FacilityId == ownerFacility.Id);
        if (!await bookingQuery.AnyAsync(ct))
            return Result<IReadOnlyList<CareHomeInternalBookingNoteItem>>.Failure(CareHomeRevenueProjection.NotFound);
        var notes = await db.InternalBookingNotes.AsNoTracking().Where(x => x.BookingId == bookingId)
            .OrderBy(x => x.CreatedOnUtc).ThenBy(x => x.Id).Select(x => new CareHomeInternalBookingNoteItem(
                x.Id, x.BookingId, x.Author.Value, x.Text, x.CreatedOnUtc)).ToListAsync(ct);
        return Result<IReadOnlyList<CareHomeInternalBookingNoteItem>>.Success(notes);
    }

    public static async Task<Result<CareHomeInternalBookingNoteItem>> Add(
        ICareHomesDbContext db, Guid bookingId, UserId actor, string text, DateTime now,
        UserId? ownerUserId, CancellationToken ct)
    {
        CareHomeFacility? ownerFacility = ownerUserId is UserId owner
            ? await db.Facilities.SingleOrDefaultAsync(x => x.OwnerUserId == owner, ct)
            : null;
        if (ownerUserId is not null && ownerFacility is null)
            return Result<CareHomeInternalBookingNoteItem>.Failure(CareHomeRevenueProjection.NotFound);
        var bookingQuery = db.Bookings.Where(x => x.Id == bookingId);
        if (ownerFacility is not null) bookingQuery = bookingQuery.Where(x => x.FacilityId == ownerFacility.Id);
        if (!await bookingQuery.AnyAsync(ct)) return Result<CareHomeInternalBookingNoteItem>.Failure(CareHomeRevenueProjection.NotFound);
        CareHomeInternalBookingNote note;
        try { note = CareHomeInternalBookingNote.Create(bookingId, actor, text, now); }
        catch (ArgumentException) { return Result<CareHomeInternalBookingNoteItem>.Failure(new("CareHomes.Bookings.InvalidNote", "A note between 1 and 4000 characters is required.")); }
        db.InternalBookingNotes.Add(note);
        await db.SaveChangesAsync(ct);
        return Result<CareHomeInternalBookingNoteItem>.Success(new(note.Id, note.BookingId, note.Author.Value, note.Text, note.CreatedOnUtc));
    }
}

public sealed class GetOwnerCareHomeInternalBookingNotesHandler(ICareHomesDbContext db)
    : IQueryHandler<GetOwnerCareHomeInternalBookingNotesQuery, IReadOnlyList<CareHomeInternalBookingNoteItem>>
{
    public Task<Result<IReadOnlyList<CareHomeInternalBookingNoteItem>>> Handle(GetOwnerCareHomeInternalBookingNotesQuery q, CancellationToken ct) =>
        CareHomeInternalNoteHandlers.List(db, q.BookingId, q.Actor, ct);
}
public sealed class GetAdminCareHomeInternalBookingNotesHandler(ICareHomesDbContext db)
    : IQueryHandler<GetAdminCareHomeInternalBookingNotesQuery, IReadOnlyList<CareHomeInternalBookingNoteItem>>
{
    public Task<Result<IReadOnlyList<CareHomeInternalBookingNoteItem>>> Handle(GetAdminCareHomeInternalBookingNotesQuery q, CancellationToken ct) =>
        CareHomeInternalNoteHandlers.List(db, q.BookingId, null, ct);
}
public sealed class AddOwnerCareHomeInternalBookingNoteHandler(ICareHomesDbContext db)
    : ICommandHandler<AddOwnerCareHomeInternalBookingNoteCommand, CareHomeInternalBookingNoteItem>
{
    public Task<Result<CareHomeInternalBookingNoteItem>> Handle(AddOwnerCareHomeInternalBookingNoteCommand q, CancellationToken ct) =>
        CareHomeInternalNoteHandlers.Add(db, q.BookingId, q.Actor, q.Text, q.UtcNow, q.Actor, ct);
}
public sealed class AddAdminCareHomeInternalBookingNoteHandler(ICareHomesDbContext db)
    : ICommandHandler<AddAdminCareHomeInternalBookingNoteCommand, CareHomeInternalBookingNoteItem>
{
    public Task<Result<CareHomeInternalBookingNoteItem>> Handle(AddAdminCareHomeInternalBookingNoteCommand q, CancellationToken ct) =>
        CareHomeInternalNoteHandlers.Add(db, q.BookingId, q.Actor, q.Text, q.UtcNow, null, ct);
}

public static class CareHomeRevenueCsv
{
    public static byte[] Render(CareHomeRevenueReport report)
    {
        var b = new StringBuilder();
        b.AppendLine("bookingId,facilityId,stayStartDate,stayEndDate,paymentCompletedOnUtc,paymentTransactionId,amountCollectedInPeriod,refundCompletedOnUtc,refundReference,refundAmountInPeriod,netMovement,currency,bookingStatus,paymentStatus,refundStatus");
        foreach (var x in report.Transactions)
            b.AppendJoin(',', Escape(x.BookingId.ToString()), Escape(x.FacilityId.ToString()), Escape(x.StayStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                Escape(x.StayEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), Escape(x.PaymentCompletedOnUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""),
                Escape(x.PaymentTransactionId?.ToString(CultureInfo.InvariantCulture) ?? ""), Number(x.AmountCollectedInPeriod),
                Escape(x.RefundCompletedOnUtc?.ToString("O", CultureInfo.InvariantCulture) ?? ""), Escape(x.RefundReference ?? ""),
                Number(x.RefundAmountInPeriod), Number(x.NetMovement),
                Escape(x.Currency), Escape(x.BookingStatus.ToString()), Escape(x.PaymentStatus.ToString()), Escape(x.RefundStatus.ToString())).AppendLine();
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(b.ToString());
    }

    private static string Number(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string Escape(string value)
    {
        string probe = value.TrimStart();
        if (probe.Length > 0 && probe[0] is '=' or '+' or '-' or '@') value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }
}
