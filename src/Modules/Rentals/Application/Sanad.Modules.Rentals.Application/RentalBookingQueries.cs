using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Application;

/// <summary>Read models. Money crosses the wire as EGP major units; the domain keeps piastre integers.</summary>
public sealed record RentalDeductionResponse(
    Guid Id,
    string DescriptionEnglish,
    string? DescriptionArabic,
    decimal Amount,
    string EvidenceReference,
    bool IsApproved,
    DateTime? ApprovedOnUtc,
    RentalDamageDisputeState DisputeState,
    string? DisputeReason,
    DateTime? DisputeResolvedOnUtc);

public sealed record RentalRefundMovementResponse(
    Guid Id,
    RentalRefundMovementKind Kind,
    Guid RefundCaseId,
    RentalRefundDestination Destination,
    decimal Awarded,
    string? GatewayReference,
    DateTime RecordedOnUtc);

/// <summary>
/// An in-flight refund claim on the booking (B1 surfacing): the persisted
/// Finance refund-case correlation survives crashes, so both the family and
/// operations can see that a refund stands claimed and — when its outcome is
/// unknown — must be reconciled before anything further can happen (#17).
/// </summary>
public sealed record RentalPendingRefundResponse(
    RentalRefundMovementKind Kind,
    Guid RefundCaseId,
    RentalRefundDestination? Destination,
    decimal ClaimedAmount);

public sealed record RentalBookingDetailResponse(
    Guid RentalBookingId,
    Guid EquipmentId,
    string EquipmentNameEnglish,
    string EquipmentNameArabic,
    int PlannedDays,
    decimal DailyRate,
    decimal Deposit,
    decimal PrepaidRent,
    string Currency,
    RentalBookingStatus Status,
    decimal? Captured,
    DateTime CreatedOnUtc,
    DateTime? DeliveredOnUtc,
    DateTime? ReturnedOnUtc,
    DateTime? SettlementDeadlineUtc,
    DateTime? InspectionCompletedOnUtc,
    string? InspectionNotes,
    DateTime? DepositSettledOnUtc,
    IReadOnlyList<RentalDeductionResponse> DamageDeductions,
    IReadOnlyList<RentalRefundMovementResponse> RefundMovements,
    RentalPendingRefundResponse? PendingRefund);

public sealed record GetFamilyRentalBookingsQuery(Guid RequestedByUserId) : IQuery<IReadOnlyList<RentalBookingDetailResponse>>;

public sealed record GetFamilyRentalBookingDetailQuery(Guid RequestedByUserId, Guid RentalBookingId) : IQuery<RentalBookingDetailResponse?>;

public sealed record GetRentalBookingAdminDetailQuery(Guid RentalBookingId) : IQuery<RentalBookingDetailResponse?>;

public sealed class GetFamilyRentalBookingsQueryHandler(IRentalsDbContext dbContext, IFamiliesDbContext familiesDbContext)
    : IQueryHandler<GetFamilyRentalBookingsQuery, IReadOnlyList<RentalBookingDetailResponse>>
{
    public async Task<Result<IReadOnlyList<RentalBookingDetailResponse>>> Handle(
        GetFamilyRentalBookingsQuery request,
        CancellationToken cancellationToken)
    {
        var family = await familiesDbContext.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Members.Any(member => member.Id == new UserId(request.RequestedByUserId)), cancellationToken);
        if (family is null)
            return Result<IReadOnlyList<RentalBookingDetailResponse>>.Failure(RentalBookingErrors.FamilyNotFound);

        var bookings = await dbContext.RentalBookings
            .AsNoTracking()
            .Include(candidate => candidate.DamageDeductions)
            .Include(candidate => candidate.RefundMovements)
            .Where(candidate => candidate.FamilyId == family.Id.Value)
            .OrderByDescending(candidate => candidate.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        return bookings.Select(RentalBookingQueriesMapper.Map).ToList();
    }
}

public sealed class GetFamilyRentalBookingDetailQueryHandler(IRentalsDbContext dbContext, IFamiliesDbContext familiesDbContext)
    : IQueryHandler<GetFamilyRentalBookingDetailQuery, RentalBookingDetailResponse?>
{
    public async Task<Result<RentalBookingDetailResponse?>> Handle(
        GetFamilyRentalBookingDetailQuery request,
        CancellationToken cancellationToken)
    {
        var family = await familiesDbContext.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Members.Any(member => member.Id == new UserId(request.RequestedByUserId)), cancellationToken);
        if (family is null)
            return Result<RentalBookingDetailResponse?>.Failure(RentalBookingErrors.FamilyNotFound);

        RentalBooking? booking = await dbContext.RentalBookings
            .AsNoTracking()
            .Include(candidate => candidate.DamageDeductions)
            .Include(candidate => candidate.RefundMovements)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == request.RentalBookingId && candidate.FamilyId == family.Id.Value,
                cancellationToken);

        if (booking is null)
            return Result<RentalBookingDetailResponse?>.Failure(RentalBookingErrors.NotFound);

        return Result<RentalBookingDetailResponse?>.Success(RentalBookingQueriesMapper.Map(booking));
    }
}

public sealed class GetRentalBookingAdminDetailQueryHandler(IRentalsDbContext dbContext)
    : IQueryHandler<GetRentalBookingAdminDetailQuery, RentalBookingDetailResponse?>
{
    public async Task<Result<RentalBookingDetailResponse?>> Handle(
        GetRentalBookingAdminDetailQuery request,
        CancellationToken cancellationToken)
    {
        RentalBooking? booking = await dbContext.RentalBookings
            .AsNoTracking()
            .Include(candidate => candidate.DamageDeductions)
            .Include(candidate => candidate.RefundMovements)
            .SingleOrDefaultAsync(candidate => candidate.Id == request.RentalBookingId, cancellationToken);

        if (booking is null)
            return Result<RentalBookingDetailResponse?>.Failure(RentalBookingErrors.NotFound);

        return Result<RentalBookingDetailResponse?>.Success(RentalBookingQueriesMapper.Map(booking));
    }
}

internal static class RentalBookingQueriesMapper
{
    internal static RentalBookingDetailResponse Map(RentalBooking booking) => new(
        booking.Id,
        booking.EquipmentId,
        booking.NameEnglishSnapshot,
        booking.NameArabicSnapshot,
        booking.PlannedDays,
        booking.DailyRateMinorUnits / 100m,
        booking.DepositMinorUnits / 100m,
        booking.PrepaidRentMinorUnits / 100m,
        "EGP",
        booking.Status,
        booking.CapturedMinorUnits is { } captured ? captured / 100m : null,
        booking.CreatedOnUtc,
        booking.DeliveredOnUtc,
        booking.ReturnedOnUtc,
        booking.SettlementDeadlineUtc,
        booking.InspectionCompletedOnUtc,
        booking.InspectionNotes,
        booking.DepositSettledOnUtc,
        booking.DamageDeductions
            .OrderBy(deduction => deduction.RecordedOnUtc)
            .Select(deduction => new RentalDeductionResponse(
                deduction.Id,
                deduction.DescriptionEnglish,
                deduction.DescriptionArabic,
                deduction.AmountMinorUnits / 100m,
                deduction.EvidenceReference,
                deduction.ApprovedOnUtc is not null,
                deduction.ApprovedOnUtc,
                deduction.DisputeState,
                deduction.DisputeReason,
                deduction.DisputeResolvedOnUtc))
            .ToList(),
        booking.RefundMovements
            .OrderBy(movement => movement.RecordedOnUtc)
            .Select(movement => new RentalRefundMovementResponse(
                movement.Id,
                movement.Kind,
                movement.RefundCaseId,
                movement.Destination,
                movement.AmountMinorUnits / 100m,
                movement.GatewayReference,
                movement.RecordedOnUtc))
            .ToList(),
        booking.PendingRefundKind is { } pendingKind && booking.PendingRefundCaseId is { } pendingCase
            ? new RentalPendingRefundResponse(
                pendingKind,
                pendingCase,
                booking.PendingRefundDestination,
                (booking.PendingRefundAmountMinorUnits ?? 0) / 100m)
            : null);
}
