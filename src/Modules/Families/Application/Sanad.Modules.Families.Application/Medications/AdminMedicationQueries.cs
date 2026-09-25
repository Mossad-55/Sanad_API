using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Medications;

namespace Sanad.Modules.Families.Application.Medications;

public sealed record AdminMedicationRecord(
    MedicationResponse Medication,
    Guid FamilyId,
    string ElderlyArabicName,
    string ElderlyEnglishName);
public sealed record PagedAdminMedicationRecords(IReadOnlyList<AdminMedicationRecord> Items, int Page, int PageSize, int TotalCount);
public sealed record AdminMedicationAdherenceAggregate(int TotalDoses, int TakenDoses, int SkippedDoses, int MissedDoses, int ScheduledDoses, decimal AdherenceRate);

public sealed record GetAdminMedicationsQuery(
    UserId ActorUserId, string ActorAccountType, string CorrelationId,
    int Page = 1, int PageSize = 20, Guid? DependentId = null,
    MedicationStatus? Status = null, string? Search = null) : IQuery<PagedAdminMedicationRecords>;

public sealed record GetAdminMedicationQuery(
    UserId ActorUserId, string ActorAccountType, string CorrelationId, MedicationId MedicationId) : IQuery<AdminMedicationRecord>;

public sealed record GetAdminMedicationDoseTimelineQuery(
    UserId ActorUserId, string ActorAccountType, string CorrelationId, MedicationId MedicationId,
    DateOnly StartDate, DateOnly EndDate, DoseStatus? Status = null) : IQuery<IReadOnlyList<MedicationDoseResponse>>;

public sealed record GetAdminMedicationAdherenceQuery(
    UserId ActorUserId, string ActorAccountType, string CorrelationId,
    DateOnly? StartDate = null, DateOnly? EndDate = null, Guid? DependentId = null) : IQuery<AdminMedicationAdherenceAggregate>;

internal static class AdminMedicationAccess
{
    public static async Task AuditAsync(IFamiliesDbContext db, UserId actor, string accountType, string action, Guid? resourceId, string correlationId, CancellationToken ct)
    {
        db.AdminMedicationAccessAudits.Add(AdminMedicationAccessAudit.Create(actor, accountType, action, "Medication", resourceId, DateTime.UtcNow, correlationId));
        await db.SaveChangesAsync(ct);
    }

    public static bool IsValidRange(DateOnly start, DateOnly end) => end >= start && end.DayNumber - start.DayNumber < 31;
}

public sealed class GetAdminMedicationsQueryHandler : IQueryHandler<GetAdminMedicationsQuery, PagedAdminMedicationRecords>
{
    private readonly IFamiliesDbContext _db;
    public GetAdminMedicationsQueryHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<PagedAdminMedicationRecords>> Handle(GetAdminMedicationsQuery request, CancellationToken ct)
    {
        await AdminMedicationAccess.AuditAsync(_db, request.ActorUserId, request.ActorAccountType, "ListMedications", null, request.CorrelationId, ct);
        int page = Math.Max(1, request.Page); int pageSize = request.PageSize is < 1 or > 100 ? 20 : request.PageSize;
        IQueryable<Medication> query = _db.Medications.AsNoTracking();
        if (request.DependentId.HasValue) query = query.Where(x => x.ElderlyId.Value == request.DependentId.Value);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        if (!string.IsNullOrWhiteSpace(request.Search)) query = query.Where(x => x.Name.ToLower().Contains(request.Search.Trim().ToLower()));
        int total = await query.CountAsync(ct);
        long offset = ((long)page - 1) * pageSize;
        List<Medication> medications = offset > int.MaxValue
            ? []
            : await query.OrderByDescending(x => x.UpdatedOnUtc)
                .Skip((int)offset)
                .Take(pageSize)
                .ToListAsync(ct);
        Guid[] dependentIds = medications.Select(x => x.ElderlyId.Value).Distinct().ToArray();
        var elderly = await _db.Elderlies.AsNoTracking()
            .Where(x => dependentIds.Contains(x.Id.Value))
            .Select(x => new
            {
                Id = x.Id.Value,
                FamilyId = x.FamilyId.Value,
                ArabicName = x.ArabicFullName.Value,
                EnglishName = x.EnglishFullName.Value
            })
            .ToDictionaryAsync(x => x.Id, ct);
        var items = medications.Select(m => new AdminMedicationRecord(
            m.ToResponse(),
            elderly[m.ElderlyId.Value].FamilyId,
            elderly[m.ElderlyId.Value].ArabicName,
            elderly[m.ElderlyId.Value].EnglishName)).ToList();
        return new PagedAdminMedicationRecords(items, page, pageSize, total);
    }
}

public sealed class GetAdminMedicationQueryHandler : IQueryHandler<GetAdminMedicationQuery, AdminMedicationRecord>
{
    private static readonly Error NotFound = new("Families.AdminMedication.NotFound", "Medication was not found.");
    private readonly IFamiliesDbContext _db;
    public GetAdminMedicationQueryHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<AdminMedicationRecord>> Handle(GetAdminMedicationQuery request, CancellationToken ct)
    {
        await AdminMedicationAccess.AuditAsync(_db, request.ActorUserId, request.ActorAccountType, "GetMedication", request.MedicationId.Value, request.CorrelationId, ct);
        var medication = await _db.Medications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.MedicationId, ct);
        if (medication is null) return Result<AdminMedicationRecord>.Failure(NotFound);
        var elderly = await _db.Elderlies.AsNoTracking()
            .Where(x => x.Id == medication.ElderlyId)
            .Select(x => new
            {
                FamilyId = x.FamilyId.Value,
                ArabicName = x.ArabicFullName.Value,
                EnglishName = x.EnglishFullName.Value
            })
            .SingleOrDefaultAsync(ct);
        if (elderly is null) return Result<AdminMedicationRecord>.Failure(NotFound);
        return new AdminMedicationRecord(
            medication.ToResponse(), elderly.FamilyId, elderly.ArabicName, elderly.EnglishName);
    }
}

public sealed class GetAdminMedicationDoseTimelineQueryHandler : IQueryHandler<GetAdminMedicationDoseTimelineQuery, IReadOnlyList<MedicationDoseResponse>>
{
    private static readonly Error InvalidRange = new("Families.AdminMedication.InvalidDateRange", "The dose timeline must use an inclusive range of 31 days or fewer.");
    private readonly IFamiliesDbContext _db;
    public GetAdminMedicationDoseTimelineQueryHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<IReadOnlyList<MedicationDoseResponse>>> Handle(GetAdminMedicationDoseTimelineQuery request, CancellationToken ct)
    {
        if (!AdminMedicationAccess.IsValidRange(request.StartDate, request.EndDate)) return Result<IReadOnlyList<MedicationDoseResponse>>.Failure(InvalidRange);
        await AdminMedicationAccess.AuditAsync(_db, request.ActorUserId, request.ActorAccountType, "GetMedicationDoseTimeline", request.MedicationId.Value, request.CorrelationId, ct);
        var medication = await _db.Medications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.MedicationId, ct);
        if (medication is null) return Result<IReadOnlyList<MedicationDoseResponse>>.Failure(new Error("Families.AdminMedication.NotFound", "Medication was not found."));
        IQueryable<MedicationDoseLog> query = _db.MedicationDoseLogs.AsNoTracking().Where(x => x.MedicationId == request.MedicationId && x.ScheduledDate >= request.StartDate && x.ScheduledDate <= request.EndDate);
        if (request.Status.HasValue) query = query.Where(x => x.Status == request.Status.Value);
        var logs = await query.OrderBy(x => x.ScheduledDate).ThenBy(x => x.ScheduledTime).ToListAsync(ct);
        return logs.Select(x => new MedicationDoseResponse(x.Id.Value, medication.Id.Value, medication.Name, medication.Dosage, medication.DoseUnit, medication.DoseQuantity, medication.Instructions, x.ScheduledDate, x.ScheduledTime, x.Status, x.TakenAtUtc, x.SkippedAtUtc, x.Notes, x.LoggedByUserId?.Value)).ToList();
    }
}

public sealed class GetAdminMedicationAdherenceQueryHandler : IQueryHandler<GetAdminMedicationAdherenceQuery, AdminMedicationAdherenceAggregate>
{
    private readonly IFamiliesDbContext _db;
    public GetAdminMedicationAdherenceQueryHandler(IFamiliesDbContext db) => _db = db;
    public async Task<Result<AdminMedicationAdherenceAggregate>> Handle(GetAdminMedicationAdherenceQuery request, CancellationToken ct)
    {
        await AdminMedicationAccess.AuditAsync(_db, request.ActorUserId, request.ActorAccountType, "GetMedicationAdherence", null, request.CorrelationId, ct);
        IQueryable<MedicationDoseLog> query = _db.MedicationDoseLogs.AsNoTracking();
        if (request.DependentId.HasValue) query = query.Where(x => x.ElderlyId.Value == request.DependentId.Value);
        if (request.StartDate.HasValue) query = query.Where(x => x.ScheduledDate >= request.StartDate.Value);
        if (request.EndDate.HasValue) query = query.Where(x => x.ScheduledDate <= request.EndDate.Value);
        var statuses = await query.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int Count(DoseStatus status) => statuses.FirstOrDefault(x => x.Key == status)?.Count ?? 0;
        int total = statuses.Sum(x => x.Count); int taken = Count(DoseStatus.Taken);
        return new AdminMedicationAdherenceAggregate(total, taken, Count(DoseStatus.Skipped), Count(DoseStatus.Missed), Count(DoseStatus.Scheduled), total == 0 ? 0 : Math.Round((decimal)taken / total * 100, 2));
    }
}
