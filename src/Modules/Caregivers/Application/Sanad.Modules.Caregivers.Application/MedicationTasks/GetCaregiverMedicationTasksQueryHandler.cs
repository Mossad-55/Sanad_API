using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Medications;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Caregivers.Application.MedicationTasks;

public sealed class GetCaregiverMedicationTasksQueryHandler(
    ICaregiversDbContext dbContext,
    IFamiliesDbContext familiesDb) : IQueryHandler<
    GetCaregiverMedicationTasksQuery,
    CaregiverMedicationTaskResponse[]>
{
    public async Task<Result<CaregiverMedicationTaskResponse[]>> Handle(
        GetCaregiverMedicationTasksQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId),
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverMedicationTaskResponse[]>.Failure(
                new Error("Caregivers.NotFound", "Caregiver not found."));
        }

        if (caregiver.UserId != request.ActorUserId)
        {
            return Result<CaregiverMedicationTaskResponse[]>.Failure(
                new Error("Caregivers.AccessDenied", "A caregiver can only view their own medication tasks."));
        }

        var elderlyIds = await familiesDb.Bookings
            .AsNoTracking()
            .Where(b => b.CaregiverId == caregiver.Id &&
                        (b.Status == BookingStatus.Confirmed ||
                         b.Status == BookingStatus.InProgress))
            .Select(b => b.ElderlyId)
                    .Distinct()
            .ToListAsync(cancellationToken);

        if (elderlyIds.Count == 0)
        {
            return Result<CaregiverMedicationTaskResponse[]>.Success([]);
        }

        var query = familiesDb.MedicationDoseLogs
            .AsNoTracking()
            .Join(
                familiesDb.Medications.AsNoTracking(),
                dose => dose.MedicationId,
                medication => medication.Id,
                (dose, medication) => new { Dose = dose, Medication = medication })
            .Join(
                familiesDb.Elderlies.AsNoTracking(),
                row => row.Medication.ElderlyId,
                elderly => elderly.Id,
                (row, elderly) => new { row.Dose, row.Medication, Elderly = elderly })
            .Where(x => elderlyIds.Contains(x.Elderly.Id));

        if (request.startDate.HasValue)
        {
            query = query.Where(x => x.Dose.ScheduledDate >= request.startDate.Value);
        }

        if (request.endDate.HasValue)
        {
            query = query.Where(x => x.Dose.ScheduledDate <= request.endDate.Value);
        }

        var rows = await query
            .OrderByDescending(x => x.Dose.ScheduledDate)
            .ThenByDescending(x => x.Dose.ScheduledTime)
            .ToListAsync(cancellationToken);

        var tasks = rows
            .Select(x =>
            {
                var status = x.Dose.Status switch
                {
                    DoseStatus.Taken => MedicationTaskStatus.Administered,
                    DoseStatus.Skipped => MedicationTaskStatus.Skipped,
                    DoseStatus.Missed => MedicationTaskStatus.Overdue,
                    _ when x.Dose.ScheduledDate < DateOnly.FromDateTime(DateTime.UtcNow) =>
                        MedicationTaskStatus.Overdue,
                    _ => MedicationTaskStatus.Pending
                };

                return new CaregiverMedicationTaskResponse(
                    x.Dose.Id.Value,
                    x.Medication.Id.Value,
                    x.Medication.Name,
                    x.Medication.Name,
                    $"{x.Medication.Dosage} {x.Medication.DoseUnit}",
                    string.Join(", ", x.Medication.DoseTimes.Select(time => time.ToString("HH:mm"))),
                    x.Dose.ScheduledDate,
                    x.Dose.ScheduledTime,
                    x.Elderly.TimeZoneId,
                    status,
                    x.Dose.Status == DoseStatus.Taken,
                    x.Dose.Status == DoseStatus.Skipped);
            })
            .Where(task => !request.Status.HasValue || task.Status == request.Status.Value)
            .ToArray();

        return Result<CaregiverMedicationTaskResponse[]>.Success(tasks);
    }
}
