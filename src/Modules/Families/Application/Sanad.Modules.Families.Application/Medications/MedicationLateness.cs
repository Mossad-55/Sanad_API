using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Medications;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Families.Application.Medications;

public sealed record MedicationLatenessEvaluationResponse(int ThresholdMinutes, int SettingVersion, DateOnly LocalDate, IReadOnlyList<MedicationDoseResponse> MissedDoses);
public sealed record EvaluateOwnMedicationLatenessCommand(UserId UserId, DateTime UtcNow) : ICommand<MedicationLatenessEvaluationResponse>;
public sealed record EvaluateAdminMedicationLatenessCommand(UserId ActorUserId, ElderlyId ElderlyId, string ActorAccountType, string CorrelationId, DateTime UtcNow) : ICommand<MedicationLatenessEvaluationResponse>;

internal static class MedicationLatenessEvaluation
{
    public static async Task<Result<MedicationLatenessEvaluationResponse>> RunAsync(
        IFamiliesDbContext db, IMedicationLatenessSettingGateway settings, IMedicationLateAlertGateway alerts,
        Elderly elderly, DateTime utcNow, CancellationToken ct)
    {
        if (utcNow.Kind != DateTimeKind.Utc) return Result<MedicationLatenessEvaluationResponse>.Failure(new Error("Families.Medication.InvalidEvaluationTime", "Evaluation time must be UTC."));
        var setting = await settings.GetActiveAsync(ct);
        if (setting is null) return Result<MedicationLatenessEvaluationResponse>.Failure(new Error("Families.Medication.LatenessSettingUnavailable", "The medication lateness setting is unavailable."));
        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(elderly.TimeZoneId); }
        catch { return Result<MedicationLatenessEvaluationResponse>.Failure(new Error("Families.Medication.InvalidProfileTimeZone", "The elderly profile time zone is invalid.")); }
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, zone);
        var localDate = DateOnly.FromDateTime(localNow);
        var medications = await db.Medications.Where(x => x.ElderlyId == elderly.Id && x.Status == MedicationStatus.Active && x.StartDate <= localDate && (!x.EndDate.HasValue || x.EndDate >= localDate)).ToListAsync(ct);
        var logs = await db.MedicationDoseLogs.Where(x => x.ElderlyId == elderly.Id && x.ScheduledDate == localDate).ToListAsync(ct);
        var missed = new List<MedicationDoseResponse>();
        var dueSlots = new List<(Medication Medication, TimeOnly Time)>();
        foreach (var medication in medications)
        foreach (var time in medication.DoseTimes)
        {
            if (localNow.TimeOfDay < time.ToTimeSpan().Add(TimeSpan.FromMinutes(setting.ThresholdMinutes))) continue;
            dueSlots.Add((medication, time));
            var log = logs.FirstOrDefault(x => x.MedicationId == medication.Id && x.ScheduledTime == time);
            if (log is null)
            {
                log = MedicationDoseLog.CreateScheduled(medication.Id, elderly.Id, localDate, time);
                db.MedicationDoseLogs.Add(log);
                logs.Add(log);
            }
            if (log.Status == DoseStatus.Scheduled) log.MarkAsMissed(utcNow);
            if (log.Status != DoseStatus.Missed) continue;
            missed.Add(ToResponse(log, medication, localDate, time));
        }
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Another evaluator may have materialized the same dose first. Detach
            // our failed inserts, verify every due slot now exists, and replay
            // from committed state instead of leaking a unique-key conflict.
            if (db is DbContext context)
            {
                foreach (var entry in context.ChangeTracker.Entries<MedicationDoseLog>()
                    .Where(x => x.State == EntityState.Added)
                    .ToList())
                    entry.State = EntityState.Detached;
            }

            logs = await db.MedicationDoseLogs
                .Where(x => x.ElderlyId == elderly.Id && x.ScheduledDate == localDate)
                .ToListAsync(ct);
            if (dueSlots.Any(slot => logs.All(x => x.MedicationId != slot.Medication.Id || x.ScheduledTime != slot.Time)))
                throw;

            missed.Clear();
            foreach (var (medication, time) in dueSlots)
            {
                var log = logs.First(x => x.MedicationId == medication.Id && x.ScheduledTime == time);
                if (log.Status == DoseStatus.Missed)
                    missed.Add(ToResponse(log, medication, localDate, time));
            }
        }
        foreach (var dose in missed) await alerts.CreateAsync(new MedicationLateAlertRequest(elderly.IdentityUserId, elderly.Id.Value, dose.MedicationId, dose.ScheduledDate, dose.ScheduledTime, utcNow), ct);
        return new MedicationLatenessEvaluationResponse(setting.ThresholdMinutes, setting.Version, localDate, missed);

        static MedicationDoseResponse ToResponse(MedicationDoseLog log, Medication medication, DateOnly date, TimeOnly time) =>
            new(log.Id.Value, medication.Id.Value, medication.Name, medication.Dosage, medication.DoseUnit, medication.DoseQuantity, medication.Instructions, date, time, log.Status, log.TakenAtUtc, log.SkippedAtUtc, log.Notes, log.LoggedByUserId?.Value);
    }
}

public sealed class EvaluateOwnMedicationLatenessCommandHandler(IFamiliesDbContext db, IMedicationLatenessSettingGateway settings, IMedicationLateAlertGateway alerts) : ICommandHandler<EvaluateOwnMedicationLatenessCommand, MedicationLatenessEvaluationResponse>
{
    public async Task<Result<MedicationLatenessEvaluationResponse>> Handle(EvaluateOwnMedicationLatenessCommand r, CancellationToken ct)
    {
        var elderly = await db.Elderlies.SingleOrDefaultAsync(x => x.IdentityUserId == r.UserId, ct);
        return elderly is null ? MedicationErrors.AccessDenied : await MedicationLatenessEvaluation.RunAsync(db, settings, alerts, elderly, r.UtcNow, ct);
    }
}

public sealed class EvaluateAdminMedicationLatenessCommandHandler(IFamiliesDbContext db, IMedicationLatenessSettingGateway settings, IMedicationLateAlertGateway alerts) : ICommandHandler<EvaluateAdminMedicationLatenessCommand, MedicationLatenessEvaluationResponse>
{
    public async Task<Result<MedicationLatenessEvaluationResponse>> Handle(EvaluateAdminMedicationLatenessCommand r, CancellationToken ct)
    {
        await AdminMedicationAccess.AuditAsync(db, r.ActorUserId, r.ActorAccountType, "EvaluateMedicationLateness", r.ElderlyId.Value, r.CorrelationId, ct);
        var elderly = await db.Elderlies.SingleOrDefaultAsync(x => x.Id == r.ElderlyId && db.Families.Any(f => f.Id == x.FamilyId && f.DeletedOnUtc == null), ct);
        return elderly is null ? Result<MedicationLatenessEvaluationResponse>.Failure(new Error("Families.AdminMedication.NotFound", "Elderly profile was not found.")) : await MedicationLatenessEvaluation.RunAsync(db, settings, alerts, elderly, r.UtcNow, ct);
    }
}
