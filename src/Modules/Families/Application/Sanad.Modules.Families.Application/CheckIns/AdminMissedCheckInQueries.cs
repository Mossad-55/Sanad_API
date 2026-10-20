using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.CheckIns;

public sealed record MissedCheckInItem(Guid ElderlyId, Guid FamilyId, DateOnly LocalDate, DateTime DueOnUtc);

public sealed record PagedMissedCheckIns(DateTime EvaluationOnUtc, IReadOnlyList<MissedCheckInItem> Items, int Page, int PageSize, int TotalCount);

public sealed record GetAdminMissedCheckInsQuery(
    UserId ActorUserId,
    string ActorAccountType,
    string CorrelationId,
    DateOnly StartDate,
    DateOnly EndDate,
    int Page = 1,
    int PageSize = 20,
    Guid? ElderlyId = null) : IQuery<PagedMissedCheckIns>;

public sealed class GetAdminMissedCheckInsQueryHandler(
    IFamiliesDbContext db,
    IFamilyIdentityGateway identityGateway,
    IDateTimeProvider clock) : IQueryHandler<GetAdminMissedCheckInsQuery, PagedMissedCheckIns>
{
    private const int MaxRangeDays = 31;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;
    private const int ActiveStatus = 2;
    private sealed record CohortProfile(ElderlyId Id, UserId IdentityUserId, FamilyId FamilyId, DateTime CreatedOnUtc, string TimeZoneId);

    public async Task<Result<PagedMissedCheckIns>> Handle(GetAdminMissedCheckInsQuery r, CancellationToken ct)
    {
        if (r.EndDate == DateOnly.MaxValue || r.EndDate < r.StartDate || (r.EndDate.DayNumber - r.StartDate.DayNumber) >= MaxRangeDays)
            return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.InvalidDateRange", "The check-in range must be an inclusive range of 31 days or fewer."));

        try
        {
            await AdminElderlyCheckInAccess.AuditAsync(db, r.ActorUserId, r.ActorAccountType, "ListMissedCheckIns", null, r.CorrelationId, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.EvaluationUnavailable", "Evaluation is temporarily unavailable."));
        }

        var evaluationOnUtc = clock.UtcNow;

        try
        {
            var elderlyQuery = db.Elderlies.AsNoTracking()
                .Where(e => db.Families.Any(f => f.Id == e.FamilyId && f.DeletedOnUtc == null));
            if (r.ElderlyId.HasValue)
                elderlyQuery = elderlyQuery.Where(e => e.Id == new ElderlyId(r.ElderlyId.Value));

            // Read only the cohort facts, never load clinical/profile fields. Identity
            // lookups below are batched; final counts necessarily cover the full cohort.
            var elderlies = await elderlyQuery
                .Select(e => new CohortProfile(e.Id, e.IdentityUserId, e.FamilyId, e.CreatedOnUtc, e.TimeZoneId))
                .ToListAsync(ct);
            const int batchSize = 200;

            if (elderlies.Count == 0)
            {
                int pageEmpty = Math.Max(1, r.Page);
                int sizeEmpty = r.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : r.PageSize;
                return new PagedMissedCheckIns(evaluationOnUtc, [], pageEmpty, sizeEmpty, 0);
            }

            var distinctIds = elderlies.Select(e => e.IdentityUserId).Distinct().ToList();
            var profileMap = new Dictionary<UserId, int>();
            try
            {
                foreach (var batch in distinctIds.Chunk(batchSize))
                {
                    var profiles = await identityGateway.GetFamilyMemberProfilesAsync(batch, ct);
                    foreach (var profile in profiles) profileMap[profile.UserId] = profile.Status;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.EvaluationUnavailable", "Evaluation is temporarily unavailable."));
            }

            // Missing profile for any requested elderly => indeterminate.
            foreach (var e in elderlies)
            {
                if (!profileMap.ContainsKey(e.IdentityUserId))
                    return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.IndeterminateEvaluation", "Evaluation is indeterminate."));
            }

            // Eligible cohort: non-deleted family + Active identity status.
            var eligible = new List<CohortProfile>();
            foreach (var e in elderlies)
            {
                if (profileMap[e.IdentityUserId] == ActiveStatus)
                    eligible.Add(e);
            }

            if (eligible.Count == 0)
            {
                int page0 = Math.Max(1, r.Page);
                int size0 = r.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : r.PageSize;
                return new PagedMissedCheckIns(evaluationOnUtc, [], page0, size0, 0);
            }

            // Load check-ins for eligible elderlies within requested range.
            var eligibleIds = eligible.Select(e => e.Id).ToHashSet();
            var checkIns = await db.ElderlyCheckIns.AsNoTracking()
                .Where(c => eligibleIds.Contains(c.ElderlyId) && c.LocalDate >= r.StartDate && c.LocalDate <= r.EndDate)
                .ToListAsync(ct);

            var answered = new HashSet<(Guid ElderlyId, DateOnly LocalDate)>(
                checkIns.Where(c => c.AnsweredOnUtc <= evaluationOnUtc)
                    .Select(c => (c.ElderlyId.Value, c.LocalDate)));

            // Build missing list.
            var missing = new List<(CohortProfile E, DateOnly Date, DateTime DueUtc)>();
            foreach (var e in eligible)
            {
                TimeZoneInfo zone;
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(e.TimeZoneId); }
                catch { return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.IndeterminateEvaluation", "Evaluation is indeterminate.")); }

                if (e.CreatedOnUtc.Kind != DateTimeKind.Utc)
                    return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.IndeterminateEvaluation", "Evaluation is indeterminate."));

                DateTime localCreated;
                try { localCreated = TimeZoneInfo.ConvertTimeFromUtc(e.CreatedOnUtc, zone); }
                catch { return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.IndeterminateEvaluation", "Evaluation is indeterminate.")); }

                var firstExpected = DateOnly.FromDateTime(localCreated).AddDays(1);

                for (var d = r.StartDate; d <= r.EndDate; d = d.AddDays(1))
                {
                    if (d < firstExpected) continue;

                    DateTime deadlineLocal = d.AddDays(1).ToDateTime(TimeOnly.MinValue);
                    if (zone.IsAmbiguousTime(deadlineLocal) || zone.IsInvalidTime(deadlineLocal))
                        return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.IndeterminateEvaluation", "Evaluation is indeterminate."));
                    DateTime deadlineUtc;
                    try { deadlineUtc = TimeZoneInfo.ConvertTimeToUtc(deadlineLocal, zone); }
                    catch { return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.IndeterminateEvaluation", "Evaluation is indeterminate.")); }

                    if (evaluationOnUtc < deadlineUtc) continue; // not yet due

                    if (answered.Contains((e.Id.Value, d))) continue;

                    missing.Add((e, d, deadlineUtc));
                }
            }

            var items = missing
                .Select(m => new MissedCheckInItem(m.E.Id.Value, m.E.FamilyId.Value, m.Date, m.DueUtc))
                .OrderByDescending(x => x.LocalDate)
                .ThenBy(x => x.ElderlyId)
                .ToList();

            int totalCount = items.Count;
            int page = Math.Max(1, r.Page);
            int size = r.PageSize is < 1 or > MaxPageSize ? DefaultPageSize : r.PageSize;
            long offset = ((long)page - 1) * size;
            var paged = offset >= totalCount ? [] : items.Skip((int)offset).Take(size).ToList();

            return new PagedMissedCheckIns(evaluationOnUtc, paged, page, size, totalCount);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            return Result<PagedMissedCheckIns>.Failure(new Error("Families.AdminCheckIn.EvaluationUnavailable", "Evaluation is temporarily unavailable."));
        }
    }
}
