using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Domain.Bookings;

namespace Sanad.Modules.CareHomes.Application.Bookings;

public sealed record AdminCareHomeBookingListItem(
    Guid BookingId, Guid FacilityId, Guid RoomTypeId, string ResidentArabicName, string ResidentEnglishName,
    DateOnly StartDate, DateOnly EndDate, CareHomeBookingStatus BookingStatus,
    CareHomeBookingPaymentStatus PaymentStatus, CareHomeRefundStatus RefundStatus,
    decimal TotalAmount, string Currency, DateTime CreatedOnUtc, DateTime? PaymentCompletedOnUtc,
    DateTime? RefundCompletedOnUtc, decimal? RefundAmount, Guid? AssignedRoomId, Guid? AssignedBedId);

public sealed record AdminCareHomeBookingPage(int Page, int PageSize, int TotalCount,
    IReadOnlyList<AdminCareHomeBookingListItem> Items);

public sealed record ListAdminCareHomeBookingsQuery(
    int Page = 1, int PageSize = 25, Guid? FacilityId = null,
    CareHomeBookingStatus? BookingStatus = null,
    CareHomeBookingPaymentStatus? PaymentStatus = null,
    CareHomeRefundStatus? RefundStatus = null,
    DateOnly? StayFrom = null, DateOnly? StayTo = null, string? Search = null)
    : IQuery<AdminCareHomeBookingPage>;

public sealed record AdminCareHomeBookingAssignment(
    Guid? FromRoomId, Guid? FromBedId, Guid ToRoomId, Guid? ToBedId,
    DateOnly EffectiveDate, Guid ActorUserId, DateTime OccurredOnUtc);

public sealed record AdminCareHomeBookingDispute(
    Guid DisputeId, CareHomeCheckInDisputeStatus Status, DateTime? CheckoutOnUtc,
    Guid OpenedByUserId, DateTime OpenedOnUtc, Guid? ResolvedByUserId,
    DateTime? ResolvedOnUtc, DateTime? EffectiveCheckInOnUtc,
    string? Evidence, string? ResolutionReason, string? FamilyReason);

public sealed record AdminCareHomeBookingDetail(
    Guid BookingId, Guid FacilityId, Guid FamilyId, Guid FamilyUserId, Guid ElderlyId,
    string ResidentArabicName, string ResidentEnglishName, Guid RoomTypeId,
    DateOnly StartDate, DateOnly EndDate, DateTime EarliestArrivalUtc,
    CareHomeBookingStatus BookingStatus, CareHomeBookingPaymentStatus PaymentStatus,
    decimal BaseAmount, decimal PlatformFeeAmount, decimal TaxAmount, decimal TotalAmount,
    string Currency, int ChargeRuleVersion, string MerchantReference,
    long? PaymentTransactionId, DateTime? PaymentCompletedOnUtc,
    CareHomeRefundStatus RefundStatus, decimal? RefundAmount, string? RefundReference,
    DateTime? RefundCompletedOnUtc, Guid? RefundCompletedByUserId, string? RefundFailureReason,
    DateTime CreatedOnUtc, DateTime UpdatedOnUtc, DateTime? DecidedOnUtc, string? DecisionReason,
    DateTime? CheckoutHoldUntilUtc, DateTime? DecisionHoldUntilUtc,
    Guid? AssignedRoomId, Guid? AssignedBedId, DateTime? ActualCheckInOnUtc,
    Guid? ActualCheckInRecordedByUserId, DateTime? ActualCheckOutOnUtc,
    Guid? ActualCheckOutRecordedByUserId, DateTime? FamilyCheckInConfirmedOnUtc,
    Guid? FamilyCheckInConfirmedByUserId, Guid? ExtensionOfBookingId,
    IReadOnlyList<AdminCareHomeBookingAssignment> AssignmentHistory,
    AdminCareHomeBookingDispute? CheckInDispute);

public sealed record GetAdminCareHomeBookingQuery(Guid BookingId) : IQuery<AdminCareHomeBookingDetail>;

public static class AdminCareHomeBookingErrors
{
    public static readonly Error InvalidQuery = new("CareHomes.Admin.InvalidQuery", "The booking query is invalid.");
    public static readonly Error NotFound = new("CareHomes.Bookings.NotFound", "Booking was not found.");
}

public sealed class ListAdminCareHomeBookingsHandler(ICareHomesDbContext db)
    : IQueryHandler<ListAdminCareHomeBookingsQuery, AdminCareHomeBookingPage>
{
    public async Task<Result<AdminCareHomeBookingPage>> Handle(ListAdminCareHomeBookingsQuery request, CancellationToken ct)
    {
        if (request.Page < 1 || request.PageSize is < 1 or > 100
            || (long)(request.Page - 1) * request.PageSize > int.MaxValue
            || request.FacilityId == Guid.Empty
            || !Valid(request.BookingStatus) || !Valid(request.PaymentStatus) || !Valid(request.RefundStatus)
            || (request.StayFrom is not null && request.StayTo is not null && request.StayTo < request.StayFrom)
            || request.Search?.Trim().Length > 128)
            return Result<AdminCareHomeBookingPage>.Failure(AdminCareHomeBookingErrors.InvalidQuery);

        IQueryable<CareHomeBooking> query = db.Bookings.AsNoTracking();
        if (request.FacilityId is Guid facilityId) query = query.Where(x => x.FacilityId.Value == facilityId);
        if (request.BookingStatus is { } bookingStatus) query = query.Where(x => x.Status == bookingStatus);
        if (request.PaymentStatus is { } paymentStatus) query = query.Where(x => x.PaymentStatus == paymentStatus);
        if (request.RefundStatus is { } refundStatus) query = query.Where(x => x.RefundStatus == refundStatus);
        if (request.StayFrom is { } stayFrom) query = query.Where(x => x.EndDate > stayFrom);
        if (request.StayTo is { } stayTo) query = query.Where(x => x.StartDate <= stayTo);
        string? search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        if (search is not null)
        {
            if (Guid.TryParse(search, out Guid bookingId)) query = query.Where(x => x.Id == bookingId);
            else query = query.Where(x => x.MerchantReference == search);
        }

        int count = await query.CountAsync(ct);
        var items = await query.OrderByDescending(x => x.CreatedOnUtc).ThenBy(x => x.Id)
            .Skip((int)((long)(request.Page - 1) * request.PageSize)).Take(request.PageSize)
            .Select(x => new AdminCareHomeBookingListItem(x.Id, x.FacilityId.Value, x.RoomTypeId,
                x.ElderlyArabicName, x.ElderlyEnglishName, x.StartDate, x.EndDate, x.Status,
                x.PaymentStatus, x.RefundStatus, x.TotalAmount, "EGP", x.CreatedOnUtc,
                x.PaymentCompletedOnUtc, x.RefundCompletedOnUtc, x.RefundAmount,
                x.AssignedRoomId, x.AssignedBedId)).ToListAsync(ct);
        return new AdminCareHomeBookingPage(request.Page, request.PageSize, count, items);
    }

    private static bool Valid<T>(T? value) where T : struct, Enum => value is null || Enum.IsDefined(value.Value);
}

public sealed class GetAdminCareHomeBookingHandler(ICareHomesDbContext db)
    : IQueryHandler<GetAdminCareHomeBookingQuery, AdminCareHomeBookingDetail>
{
    public async Task<Result<AdminCareHomeBookingDetail>> Handle(GetAdminCareHomeBookingQuery request, CancellationToken ct)
    {
        if (request.BookingId == Guid.Empty)
            return Result<AdminCareHomeBookingDetail>.Failure(AdminCareHomeBookingErrors.InvalidQuery);
        var x = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.BookingId, ct);
        if (x is null) return Result<AdminCareHomeBookingDetail>.Failure(AdminCareHomeBookingErrors.NotFound);

        var assignments = await db.BookingAssignmentHistory.AsNoTracking()
            .Where(a => a.BookingId == x.Id)
            .OrderBy(a => a.EffectiveDate).ThenBy(a => a.OccurredOnUtc).ThenBy(a => a.Id)
            .Select(a => new AdminCareHomeBookingAssignment(a.FromRoomId, a.FromBedId, a.ToRoomId,
                a.ToBedId, a.EffectiveDate, a.Actor.Value, a.OccurredOnUtc)).ToListAsync(ct);
        var disputeEntity = await db.CheckInDisputes.AsNoTracking().Where(d => d.BookingId == x.Id)
            .OrderByDescending(d => d.OpenedOnUtc).ThenBy(d => d.Id)
            .FirstOrDefaultAsync(ct);
        AdminCareHomeBookingDispute? dispute = disputeEntity is null ? null : new(
            disputeEntity.Id, disputeEntity.Status, disputeEntity.CheckoutOnUtc,
            disputeEntity.OpenedBy.Value, disputeEntity.OpenedOnUtc,
            disputeEntity.ResolvedBy?.Value, disputeEntity.ResolvedOnUtc,
            disputeEntity.EffectiveCheckInOnUtc, disputeEntity.Evidence,
            disputeEntity.Reason, disputeEntity.FamilyReason);

        return new AdminCareHomeBookingDetail(x.Id, x.FacilityId.Value, x.FamilyId.Value,
            x.FamilyUserId.Value, x.ElderlyId.Value, x.ElderlyArabicName, x.ElderlyEnglishName,
            x.RoomTypeId, x.StartDate, x.EndDate, x.EarliestArrivalUtc, x.Status, x.PaymentStatus,
            x.BaseAmount, x.PlatformFeeAmount, x.TaxAmount, x.TotalAmount, "EGP", x.ChargeRuleVersion,
            x.MerchantReference, x.PaymobTransactionId, x.PaymentCompletedOnUtc, x.RefundStatus,
            x.RefundAmount, x.RefundReference, x.RefundCompletedOnUtc, x.RefundCompletedBy?.Value,
            x.RefundFailureReason, x.CreatedOnUtc, x.UpdatedOnUtc, x.DecidedOnUtc, x.DecisionReason,
            x.CheckoutHoldUntilUtc, x.DecisionHoldUntilUtc, x.AssignedRoomId, x.AssignedBedId,
            x.ActualCheckInOnUtc, x.ActualCheckInRecordedBy?.Value, x.ActualCheckOutOnUtc,
            x.ActualCheckOutRecordedBy?.Value, x.FamilyCheckInConfirmedOnUtc,
            x.FamilyCheckInConfirmedBy?.Value, x.ExtensionOfBookingId, assignments, dispute);
    }
}
