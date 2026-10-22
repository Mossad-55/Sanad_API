using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Rentals.Domain;
using static Sanad.Modules.Rentals.Application.RentalOperationsLoader;

namespace Sanad.Modules.Rentals.Application;

public static class RentalOperationsErrors
{
    public static readonly Error BookingNotFound =
        new("Rentals.Operations.BookingNotFound", "The rental booking was not found.");

    public static readonly Error DeductionNotFound =
        new("Rentals.Operations.DeductionNotFound", "The damage deduction was not found.");

    public static readonly Error Invalid =
        new("Rentals.Operations.Invalid", "The rental operation request is not valid.");

    public static readonly Error InvalidOperation =
        new("Rentals.Operations.InvalidOperation", "The rental booking is not in a state that allows this operation.");

    public static readonly Error Conflict =
        new("Rentals.Operations.Conflict", "The rental booking changed concurrently; retry.");

    public static readonly Error Unknown =
        new("Rentals.Operations.Unknown", "The rental operation could not be completed.");
}

public sealed record RecordRentalDeliveryCommand(Guid RentalBookingId, Guid ActorUserId, DateTime UtcNow) : ICommand;

public sealed class RecordRentalDeliveryCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<RecordRentalDeliveryCommand>
{
    public async Task<Result> Handle(RecordRentalDeliveryCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalBooking? booking = await LoadBookingAsync(dbContext, request.RentalBookingId, includeDeductions: false, cancellationToken);
            if (booking is null)
                return Result.Failure(RentalOperationsErrors.BookingNotFound);

            booking.RecordDelivery(request.UtcNow, request.ActorUserId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalOperationsErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(RentalOperationsErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalOperationsErrors.Unknown);
        }
    }
}

/// <summary>
/// Records the physical return and anchors the seven-business-day deposit
/// window (Owner decision #20) with the shared Finance business-day calendar:
/// the deadline is computed in the application layer and handed to the
/// aggregate, which only guards that it is a strictly-later UTC instant.
/// </summary>
public sealed record RecordRentalReturnCommand(Guid RentalBookingId, Guid ActorUserId, DateTime UtcNow) : ICommand;

public sealed class RecordRentalReturnCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<RecordRentalReturnCommand>
{
    public async Task<Result> Handle(RecordRentalReturnCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalBooking? booking = await LoadBookingAsync(dbContext, request.RentalBookingId, includeDeductions: false, cancellationToken);
            if (booking is null)
                return Result.Failure(RentalOperationsErrors.BookingNotFound);

            DateTime settlementDeadlineUtc = BusinessDayCalendar.AddBusinessDays(request.UtcNow, DepositWindowBusinessDays);
            booking.RecordReturn(request.UtcNow, settlementDeadlineUtc, request.ActorUserId);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalOperationsErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(RentalOperationsErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalOperationsErrors.Unknown);
        }
    }

    /// <summary>Clause 8 / decision #20: seven business days (Africa/Cairo, official Egyptian holidays excluded per #36).</summary>
    internal const int DepositWindowBusinessDays = 7;
}

public sealed record CompleteRentalInspectionCommand(
    Guid RentalBookingId,
    Guid ActorUserId,
    string? Notes,
    DateTime UtcNow) : ICommand;

public sealed class CompleteRentalInspectionCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<CompleteRentalInspectionCommand>
{
    public async Task<Result> Handle(CompleteRentalInspectionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalBooking? booking = await LoadBookingAsync(dbContext, request.RentalBookingId, includeDeductions: false, cancellationToken);
            if (booking is null)
                return Result.Failure(RentalOperationsErrors.BookingNotFound);

            booking.CompleteInspection(request.UtcNow, request.ActorUserId, request.Notes);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalOperationsErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(RentalOperationsErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalOperationsErrors.Unknown);
        }
    }
}

public sealed record RecordRentalDamageDeductionCommand(
    Guid RentalBookingId,
    string DescriptionEnglish,
    string? DescriptionArabic,
    long AmountMinorUnits,
    string EvidenceReference,
    Guid ActorUserId,
    DateTime UtcNow) : ICommand<Guid>;

public sealed class RecordRentalDamageDeductionCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<RecordRentalDamageDeductionCommand, Guid>
{
    public async Task<Result<Guid>> Handle(RecordRentalDamageDeductionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalBooking? booking = await LoadBookingAsync(dbContext, request.RentalBookingId, includeDeductions: true, cancellationToken);
            if (booking is null)
                return Result<Guid>.Failure(RentalOperationsErrors.BookingNotFound);

            RentalDamageDeduction deduction = booking.AddDamageDeduction(
                request.DescriptionEnglish,
                request.DescriptionArabic,
                request.AmountMinorUnits,
                request.EvidenceReference,
                request.ActorUserId,
                request.UtcNow);

            await dbContext.SaveChangesAsync(cancellationToken);
            return Result<Guid>.Success(deduction.Id);
        }
        catch (DomainException ex)
        {
            return Result<Guid>.Failure(new Error(RentalOperationsErrors.Invalid.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result<Guid>.Failure(RentalOperationsErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result<Guid>.Failure(RentalOperationsErrors.Unknown);
        }
    }
}

/// <summary>SuperAdmin approval of a documented deduction (#21). Owned by the booking aggregate: the approval only lands while the deposit is unsettled, and it records on the booking's optimistic-concurrency token, so an approval can never silently race an concurrent settlement or refund claim (a loser surfaces as the stable conflict). A past-deadline approval stays recorded evidence that never nets (#20 — the deadline rule is applied at settlement computation).</summary>
public sealed record ApproveRentalDamageDeductionCommand(Guid DeductionId, Guid ActorUserId, DateTime UtcNow) : ICommand;

public sealed class ApproveRentalDamageDeductionCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<ApproveRentalDamageDeductionCommand>
{
    public async Task<Result> Handle(ApproveRentalDamageDeductionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalBooking? booking = await LoadBookingByDeductionAsync(dbContext, deductionId: request.DeductionId, includeDeductions: true, cancellationToken);

            if (booking is null)
                return Result.Failure(RentalOperationsErrors.DeductionNotFound);

            booking.ApproveDamageDeduction(request.DeductionId, request.ActorUserId, request.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalOperationsErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(RentalOperationsErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalOperationsErrors.Unknown);
        }
    }
}

/// <summary>
/// The customer's dispute of a deduction (Owner decision #21: the customer
/// sees amount/reason and can dispute). The dispute is recorded evidence that
/// never rewrites or deletes the deduction.
/// </summary>
public sealed record DisputeRentalDamageDeductionCommand(
    Guid RentalBookingId,
    Guid DeductionId,
    Guid RequestedByUserId,
    string? Reason,
    DateTime UtcNow) : ICommand;

public sealed class DisputeRentalDamageDeductionCommandHandler(
    IRentalsDbContext dbContext,
    IFamiliesDbContext familiesDbContext)
    : ICommandHandler<DisputeRentalDamageDeductionCommand>
{
    public async Task<Result> Handle(DisputeRentalDamageDeductionCommand request, CancellationToken cancellationToken)
    {
        try
        {
            // Only the booking's own family can dispute its deductions.
            var family = await familiesDbContext.Families
                .AsNoTracking()
                .SingleOrDefaultAsync(candidate => candidate.Members.Any(member => member.Id == new UserId(request.RequestedByUserId)), cancellationToken);
            if (family is null)
                return Result.Failure(RentalOperationsErrors.BookingNotFound);

            RentalBooking? booking = await LoadBookingAsync(dbContext, request.RentalBookingId, includeDeductions: true, cancellationToken);
            if (booking is null || booking.FamilyId != family.Id.Value)
                return Result.Failure(RentalOperationsErrors.BookingNotFound);

            RentalDamageDeduction? deduction = booking.DamageDeductions
                .FirstOrDefault(candidate => candidate.Id == request.DeductionId);
            if (deduction is null)
                return Result.Failure(RentalOperationsErrors.DeductionNotFound);

            // Routed through the booking aggregate so the dispute records on the
            // booking's optimistic-concurrency token, never silently on a child row.
            booking.RecordFamilyDamageDispute(request.DeductionId, request.RequestedByUserId, request.UtcNow, request.Reason);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalOperationsErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(RentalOperationsErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalOperationsErrors.Unknown);
        }
    }
}

/// <summary>Staff resolution note for a disputed deduction; the dispute itself is never rewritten. Routed through the booking aggregate for the same concurrency protection as the approval.</summary>
public sealed record ResolveRentalDamageDisputeCommand(Guid DeductionId, Guid ActorUserId, DateTime UtcNow) : ICommand;

public sealed class ResolveRentalDamageDisputeCommandHandler(IRentalsDbContext dbContext)
    : ICommandHandler<ResolveRentalDamageDisputeCommand>
{
    public async Task<Result> Handle(ResolveRentalDamageDisputeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            RentalBooking? booking = await LoadBookingByDeductionAsync(dbContext, deductionId: request.DeductionId, includeDeductions: true, cancellationToken);

            if (booking is null)
                return Result.Failure(RentalOperationsErrors.DeductionNotFound);

            booking.ResolveDamageDispute(request.DeductionId, request.ActorUserId, request.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DomainException ex)
        {
            return Result.Failure(new Error(RentalOperationsErrors.InvalidOperation.Code, ex.Message));
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(RentalOperationsErrors.Conflict);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return Result.Failure(RentalOperationsErrors.Unknown);
        }
    }
}

internal static class RentalOperationsLoader
{
    /// <summary>Loads the booking aggregate that owns the given damage deduction, or null when the deduction does not exist. Routing child mutations through the aggregate gives deduction approvals/resolutions the booking's optimistic-concurrency protection.</summary>
    internal static async Task<RentalBooking?> LoadBookingByDeductionAsync(
        IRentalsDbContext dbContext,
        Guid deductionId,
        bool includeDeductions,
        CancellationToken cancellationToken)
    {
        Guid bookingId = await dbContext.RentalDamageDeductions
            .Where(candidate => candidate.Id == deductionId)
            .Select(candidate => candidate.RentalBookingId)
            .FirstOrDefaultAsync(cancellationToken);

        return bookingId == Guid.Empty
            ? null
            : await LoadBookingAsync(dbContext, bookingId, includeDeductions, cancellationToken);
    }

    internal static Task<RentalBooking?> LoadBookingAsync(
        IRentalsDbContext dbContext,
        Guid rentalBookingId,
        bool includeDeductions,
        CancellationToken cancellationToken)
    {
        if (includeDeductions)
        {
            return dbContext.RentalBookings
                .Include(candidate => candidate.DamageDeductions)
                .SingleOrDefaultAsync(candidate => candidate.Id == rentalBookingId, cancellationToken);
        }

        return dbContext.RentalBookings
            .SingleOrDefaultAsync(candidate => candidate.Id == rentalBookingId, cancellationToken);
    }
}
