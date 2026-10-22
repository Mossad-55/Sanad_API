using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Rentals.Domain;

namespace Sanad.Modules.Rentals.Application;

public static class RentalBookingErrors
{
    public static readonly Error FamilyNotFound =
        new("Rentals.Booking.FamilyNotFound", "Family account not found for current user.");

    public static readonly Error NotFound =
        new("Rentals.Booking.NotFound", "The rental booking was not found.");

    public static readonly Error EquipmentNotFound =
        new("Rentals.Booking.EquipmentNotFound", "The rental equipment item was not found or is not active.");

    public static readonly Error Invalid =
        new("Rentals.Booking.Invalid", "The rental booking request is not valid.");

    public static readonly Error InvalidOperation =
        new("Rentals.Booking.InvalidOperation", "The rental booking is not in a state that allows this operation.");

    public static readonly Error Conflict =
        new("Rentals.Booking.Conflict", "The rental booking changed concurrently; retry.");

    public static readonly Error Unknown =
        new("Rentals.Booking.Unknown", "The rental booking operation could not be completed.");
}

/// <summary>
/// The executable refund destinations of this stage (Owner decision #10: the
/// original payment method by default, app credit by customer choice; the
/// bank/wallet transfer rails remain finance-ops and are not offered here).
/// </summary>
public static class RentalRefundDestinationGuard
{
    public static bool IsExecutable(RentalRefundDestination destination) =>
        destination is RentalRefundDestination.OriginalPaymentMethod or RentalRefundDestination.AppCredit;
}

public sealed record RentalBookingResponse(
    Guid RentalBookingId,
    Guid EquipmentId,
    string EquipmentNameEnglish,
    string EquipmentNameArabic,
    int PlannedDays,
    decimal DailyRate,
    decimal Deposit,
    decimal PrepaidRent,
    decimal TotalPayable,
    string Currency,
    RentalBookingStatus Status,
    DateTime CreatedOnUtc);

public sealed record CreateRentalBookingCommand(
    Guid RequestedByUserId,
    Guid EquipmentId,
    int PlannedDays,
    string? DeliveryAddress,
    DateTime UtcNow) : ICommand<RentalBookingResponse>;

public sealed class CreateRentalBookingCommandHandler(
    IRentalsDbContext dbContext,
    IFamiliesDbContext familiesDbContext)
    : ICommandHandler<CreateRentalBookingCommand, RentalBookingResponse>
{
    public async Task<Result<RentalBookingResponse>> Handle(
        CreateRentalBookingCommand request,
        CancellationToken cancellationToken)
    {
        if (request.PlannedDays < 1)
            return Result<RentalBookingResponse>.Failure(
                new Error(RentalBookingErrors.Invalid.Code, "The rental length must be at least one day."));

        try
        {
            // The caller's family resolution mirrors the Families booking
            // handlers: a member's user id resolves the owning family.
            var family = await familiesDbContext.Families
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Members.Any(member => member.Id == new UserId(request.RequestedByUserId)), cancellationToken);
            if (family is null)
                return Result<RentalBookingResponse>.Failure(RentalBookingErrors.FamilyNotFound);

            RentalEquipment? equipment = await dbContext.RentalEquipment
                .SingleOrDefaultAsync(item => item.Id == request.EquipmentId, cancellationToken);
            if (equipment is null || !equipment.IsActive)
                return Result<RentalBookingResponse>.Failure(RentalBookingErrors.EquipmentNotFound);

            RentalBooking booking = RentalBooking.Create(
                equipment,
                family.Id.Value,
                request.RequestedByUserId,
                request.PlannedDays,
                request.DeliveryAddress,
                request.UtcNow);

            dbContext.RentalBookings.Add(booking);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<RentalBookingResponse>.Success(Map(booking));
        }
        catch (DomainException ex)
        {
            return Result<RentalBookingResponse>.Failure(new Error(RentalBookingErrors.Invalid.Code, ex.Message));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<RentalBookingResponse>.Failure(RentalBookingErrors.Unknown);
        }
    }

    internal static RentalBookingResponse Map(RentalBooking booking) => new(
        booking.Id,
        booking.EquipmentId,
        booking.NameEnglishSnapshot,
        booking.NameArabicSnapshot,
        booking.PlannedDays,
        booking.DailyRateMinorUnits / 100m,
        booking.DepositMinorUnits / 100m,
        booking.PrepaidRentMinorUnits / 100m,
        (booking.PrepaidRentMinorUnits + booking.DepositMinorUnits) / 100m,
        "EGP",
        booking.Status,
        booking.CreatedOnUtc);
}
