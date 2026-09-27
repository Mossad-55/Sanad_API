using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;

namespace Sanad.Modules.Families.Application.CheckIns;

public sealed record AdminElderlyCheckInRecord(Guid Id, Guid ElderlyId, Guid FamilyId, string ElderlyArabicName, string ElderlyEnglishName, bool Answer, DateOnly LocalDate, TimeOnly AnsweredAtLocalTime, DateTime AnsweredOnUtc, Guid ActorUserId);
public sealed record PagedAdminElderlyCheckIns(IReadOnlyList<AdminElderlyCheckInRecord> Items, int Page, int PageSize, int TotalCount);
public sealed record AdminElderlyCheckInAggregate(int Total, int Positive, int Negative);
public sealed record GetAdminElderlyCheckInsQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, int Page = 1, int PageSize = 20, Guid? ElderlyId = null, DateOnly? StartDate = null, DateOnly? EndDate = null, bool? Answer = null) : IQuery<PagedAdminElderlyCheckIns>;
public sealed record GetAdminElderlyCheckInQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid CheckInId) : IQuery<AdminElderlyCheckInRecord>;
public sealed record GetAdminElderlyCheckInTimelineQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid ElderlyId, DateOnly StartDate, DateOnly EndDate) : IQuery<IReadOnlyList<AdminElderlyCheckInRecord>>;
public sealed record GetAdminElderlyCheckInAggregateQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid? ElderlyId = null, DateOnly? StartDate = null, DateOnly? EndDate = null) : IQuery<AdminElderlyCheckInAggregate>;

internal static class AdminElderlyCheckInAccess
{
    public static async Task AuditAsync(IFamiliesDbContext db, UserId actor, string accountType, string action, Guid? resourceId, string correlationId, CancellationToken ct)
    {
        db.AdminMedicationAccessAudits.Add(Domain.Medications.AdminMedicationAccessAudit.Create(actor, accountType, action, "CheckIn", resourceId, DateTime.UtcNow, correlationId));
        await db.SaveChangesAsync(ct);
    }
    public static bool ValidRange(DateOnly start, DateOnly end) => end >= start && end.DayNumber - start.DayNumber < 31;
}

internal static class AdminElderlyCheckInMapping
{
    public static AdminElderlyCheckInRecord Map(ElderlyCheckIn c, Elderly e) => new(c.Id, e.Id.Value, e.FamilyId.Value, e.ArabicFullName.Value, e.EnglishFullName.Value, c.Answer, c.LocalDate, c.AnsweredAtLocalTime, c.AnsweredOnUtc, c.AnsweredByUserId.Value);
}

public sealed class GetAdminElderlyCheckInsQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetAdminElderlyCheckInsQuery, PagedAdminElderlyCheckIns>
{
    public async Task<Result<PagedAdminElderlyCheckIns>> Handle(GetAdminElderlyCheckInsQuery r, CancellationToken ct)
    {
        if (r.StartDate.HasValue && r.EndDate.HasValue && !AdminElderlyCheckInAccess.ValidRange(r.StartDate.Value, r.EndDate.Value))
            return Result<PagedAdminElderlyCheckIns>.Failure(new Error("Families.AdminCheckIn.InvalidDateRange", "The check-in range must be an inclusive range of 31 days or fewer."));
        await AdminElderlyCheckInAccess.AuditAsync(db, r.ActorUserId, r.ActorAccountType, "ListCheckIns", null, r.CorrelationId, ct);
        int page = Math.Max(1, r.Page), size = r.PageSize is < 1 or > 100 ? 20 : r.PageSize;
        var q = db.ElderlyCheckIns.AsNoTracking().Join(db.Elderlies.AsNoTracking(), c => c.ElderlyId, e => e.Id, (c, e) => new { c, e }).Where(x => db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null));
        if (r.ElderlyId.HasValue) q = q.Where(x => x.e.Id.Value == r.ElderlyId.Value);
        if (r.StartDate.HasValue) q = q.Where(x => x.c.LocalDate >= r.StartDate.Value);
        if (r.EndDate.HasValue) q = q.Where(x => x.c.LocalDate <= r.EndDate.Value);
        if (r.Answer.HasValue) q = q.Where(x => x.c.Answer == r.Answer.Value);
        int total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(x => x.c.LocalDate).ThenByDescending(x => x.c.AnsweredOnUtc).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedAdminElderlyCheckIns(rows.Select(x => AdminElderlyCheckInMapping.Map(x.c, x.e)).ToList(), page, size, total);
    }
}

public sealed class GetAdminElderlyCheckInQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetAdminElderlyCheckInQuery, AdminElderlyCheckInRecord>
{
    public async Task<Result<AdminElderlyCheckInRecord>> Handle(GetAdminElderlyCheckInQuery r, CancellationToken ct)
    {
        await AdminElderlyCheckInAccess.AuditAsync(db, r.ActorUserId, r.ActorAccountType, "GetCheckIn", r.CheckInId, r.CorrelationId, ct);
        var x = await db.ElderlyCheckIns.AsNoTracking().Where(c => c.Id == r.CheckInId).Join(db.Elderlies.AsNoTracking(), c => c.ElderlyId, e => e.Id, (c, e) => new { c, e }).Where(x => db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null)).SingleOrDefaultAsync(ct);
        return x is null ? Result<AdminElderlyCheckInRecord>.Failure(new Error("Families.AdminCheckIn.NotFound", "Check-in was not found.")) : AdminElderlyCheckInMapping.Map(x.c, x.e);
    }
}

public sealed class GetAdminElderlyCheckInTimelineQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetAdminElderlyCheckInTimelineQuery, IReadOnlyList<AdminElderlyCheckInRecord>>
{
    public async Task<Result<IReadOnlyList<AdminElderlyCheckInRecord>>> Handle(GetAdminElderlyCheckInTimelineQuery r, CancellationToken ct)
    {
        if (!AdminElderlyCheckInAccess.ValidRange(r.StartDate, r.EndDate)) return Result<IReadOnlyList<AdminElderlyCheckInRecord>>.Failure(new Error("Families.AdminCheckIn.InvalidDateRange", "The check-in timeline must use an inclusive range of 31 days or fewer."));
        await AdminElderlyCheckInAccess.AuditAsync(db, r.ActorUserId, r.ActorAccountType, "GetCheckInTimeline", r.ElderlyId, r.CorrelationId, ct);
        var rows = await db.ElderlyCheckIns.AsNoTracking().Where(c => c.ElderlyId.Value == r.ElderlyId && c.LocalDate >= r.StartDate && c.LocalDate <= r.EndDate).Join(db.Elderlies.AsNoTracking(), c => c.ElderlyId, e => e.Id, (c, e) => new { c, e }).Where(x => db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null)).OrderBy(x => x.c.LocalDate).ToListAsync(ct);
        return rows.Select(x => AdminElderlyCheckInMapping.Map(x.c, x.e)).ToList();
    }
}

public sealed class GetAdminElderlyCheckInAggregateQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetAdminElderlyCheckInAggregateQuery, AdminElderlyCheckInAggregate>
{
    public async Task<Result<AdminElderlyCheckInAggregate>> Handle(GetAdminElderlyCheckInAggregateQuery r, CancellationToken ct)
    {
        if (r.StartDate.HasValue && r.EndDate.HasValue && !AdminElderlyCheckInAccess.ValidRange(r.StartDate.Value, r.EndDate.Value))
            return Result<AdminElderlyCheckInAggregate>.Failure(new Error("Families.AdminCheckIn.InvalidDateRange", "The check-in range must be an inclusive range of 31 days or fewer."));
        await AdminElderlyCheckInAccess.AuditAsync(db, r.ActorUserId, r.ActorAccountType, "GetCheckInAggregate", r.ElderlyId, r.CorrelationId, ct);
        var q = db.ElderlyCheckIns.AsNoTracking()
            .Join(db.Elderlies.AsNoTracking(), c => c.ElderlyId, e => e.Id, (c, e) => new { c, e })
            .Where(x => db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null))
            .Where(x => (!r.ElderlyId.HasValue || x.c.ElderlyId.Value == r.ElderlyId) && (!r.StartDate.HasValue || x.c.LocalDate >= r.StartDate) && (!r.EndDate.HasValue || x.c.LocalDate <= r.EndDate))
            .Select(x => x.c);
        var counts = await q.GroupBy(c => c.Answer).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        int positive = counts.FirstOrDefault(x => x.Key)?.Count ?? 0, negative = counts.FirstOrDefault(x => !x.Key)?.Count ?? 0;
        return new AdminElderlyCheckInAggregate(positive + negative, positive, negative);
    }
}
