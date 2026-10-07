using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Caregivers.Application.MedicationTasks;

public sealed class AdministerCaregiverMedicationTaskCommandHandler(
    ICaregiversDbContext caregiversDb,
    IFamiliesDbContext familiesDb) : ICommandHandler<
        AdministerCaregiverMedicationTaskCommand,
        CaregiverMedicationTaskActionResponse>
{
    public async Task<Result<CaregiverMedicationTaskActionResponse>> Handle(
        AdministerCaregiverMedicationTaskCommand request,
        CancellationToken cancellationToken)
    {
        var caregiver = await caregiversDb.Caregivers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == new CaregiverId(request.CaregiverId), cancellationToken);
        if (caregiver is null)
            return Denied();
        if (caregiver.UserId != request.ActorUserId)
            return Denied();

        var dose = await familiesDb.MedicationDoseLogs
            .SingleOrDefaultAsync(d => d.Id == request.TaskId, cancellationToken);
        if (dose is null)
            return Result<CaregiverMedicationTaskActionResponse>.Failure(
                new Error("Caregivers.MedicationTask.NotFound", "Medication task not found."));

        if (!await HasActiveBooking(familiesDb, caregiver.Id, dose.ElderlyId, cancellationToken))
            return Denied();

        try
        {
            dose.MarkAsTaken(request.ActorUserId, DateTime.UtcNow, request.Notes);
        }
        catch (DomainException exception)
        {
            return Result<CaregiverMedicationTaskActionResponse>.Failure(
                new Error("Caregivers.MedicationTask.InvalidOperation", exception.Message));
        }

        await familiesDb.SaveChangesAsync(cancellationToken);
        return Result<CaregiverMedicationTaskActionResponse>.Success(
            new(dose.Id.Value, MedicationTaskStatus.Administered, dose.UpdatedOnUtc));
    }

    internal static async Task<bool> HasActiveBooking(
        IFamiliesDbContext db,
        CaregiverId caregiverId,
        ElderlyId elderlyId,
        CancellationToken cancellationToken) =>
        await db.Bookings.AsNoTracking().AnyAsync(
            b => b.CaregiverId == caregiverId && b.ElderlyId == elderlyId &&
                 (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.InProgress),
            cancellationToken);

    internal static Result<CaregiverMedicationTaskActionResponse> Denied() =>
        Result<CaregiverMedicationTaskActionResponse>.Failure(
            new Error("Caregivers.AccessDenied", "The caregiver cannot access this medication task."));
}

public sealed class SkipCaregiverMedicationTaskCommandHandler(
    ICaregiversDbContext caregiversDb,
    IFamiliesDbContext familiesDb) : ICommandHandler<
        SkipCaregiverMedicationTaskCommand,
        CaregiverMedicationTaskActionResponse>
{
    public async Task<Result<CaregiverMedicationTaskActionResponse>> Handle(
        SkipCaregiverMedicationTaskCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Trim().Length > MedicationDoseLog.MaximumNotesLength)
        {
            return Result<CaregiverMedicationTaskActionResponse>.Failure(
                new Error("Caregivers.MedicationTask.InvalidReason", "A reason of 1 to 500 characters is required."));
        }

        var caregiver = await caregiversDb.Caregivers.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == new CaregiverId(request.CaregiverId), cancellationToken);
        if (caregiver is null || caregiver.UserId != request.ActorUserId)
            return AdministerCaregiverMedicationTaskCommandHandler.Denied();

        var dose = await familiesDb.MedicationDoseLogs
            .SingleOrDefaultAsync(d => d.Id == request.TaskId, cancellationToken);
        if (dose is null)
            return Result<CaregiverMedicationTaskActionResponse>.Failure(
                new Error("Caregivers.MedicationTask.NotFound", "Medication task not found."));

        if (!await AdministerCaregiverMedicationTaskCommandHandler.HasActiveBooking(
                familiesDb, caregiver.Id, dose.ElderlyId, cancellationToken))
            return AdministerCaregiverMedicationTaskCommandHandler.Denied();

        try
        {
            dose.MarkAsSkipped(request.ActorUserId, DateTime.UtcNow, request.Reason.Trim());
        }
        catch (DomainException exception)
        {
            return Result<CaregiverMedicationTaskActionResponse>.Failure(
                new Error("Caregivers.MedicationTask.InvalidOperation", exception.Message));
        }

        await familiesDb.SaveChangesAsync(cancellationToken);
        return Result<CaregiverMedicationTaskActionResponse>.Success(
            new(dose.Id.Value, MedicationTaskStatus.Skipped, dose.UpdatedOnUtc));
    }
}
