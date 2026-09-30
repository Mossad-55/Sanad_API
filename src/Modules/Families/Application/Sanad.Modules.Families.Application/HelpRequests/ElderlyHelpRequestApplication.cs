using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.HelpRequests;
using Sanad.Modules.Families.Domain.HelpRequests;

namespace Sanad.Modules.Families.Application.HelpRequests;
public sealed record CreateElderlyHelpRequestCommand(UserId ElderlyIdentityUserId, string ActorKey, string ActionKey, string NeedKey, string? QualifierKey, string? CustomText, string IdempotencyKey) : ICommand<ElderlyHelpRequestResponse>;
public sealed record CancelElderlyHelpRequestCommand(UserId ElderlyIdentityUserId, Guid RequestId, string? Reason) : ICommand<ElderlyHelpRequestResponse>;
public sealed record ChangeElderlyHelpRequestStatusCommand(UserId ActorUserId, Guid RequestId, ElderlyHelpRequestHistoryAction Action, string? Reason) : ICommand<ElderlyHelpRequestResponse>;
public sealed record GetElderlyHelpRequestsQuery(UserId ElderlyIdentityUserId, Guid? RequestId = null) : IQuery<IReadOnlyList<ElderlyHelpRequestResponse>>;
public sealed record GetElderlyHelpRequestQuery(UserId ElderlyIdentityUserId, Guid RequestId) : IQuery<ElderlyHelpRequestResponse>;
public sealed record AdminListElderlyHelpRequestsQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, ElderlyHelpRequestStatus? Status = null, Guid? ElderlyId = null, int Page = 1, int PageSize = 20) : IQuery<PagedElderlyHelpRequests>;
public sealed record AdminGetElderlyHelpRequestQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid RequestId) : IQuery<ElderlyHelpRequestResponse>;
public sealed record AdminGetElderlyHelpRequestHistoryQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid RequestId) : IQuery<IReadOnlyList<ElderlyHelpRequestHistoryResponse>>;
public sealed record AdminGetElderlyHelpRequestAggregateQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId) : IQuery<IReadOnlyDictionary<ElderlyHelpRequestStatus, int>>;
public sealed record ElderlyHelpRequestResponse(Guid Id, Guid ElderlyId, ElderlyHelpRequestStatus Status, string ActorKey, string ActionKey, string NeedKey, string? QualifierKey, string? CustomText, DateTime CreatedOnUtc, DateTime UpdatedOnUtc);
public sealed record ElderlyHelpRequestHistoryResponse(Guid Id, ElderlyHelpRequestHistoryAction Action, ElderlyHelpRequestStatus Status, string? Reason, Guid ActorUserId, DateTime OccurredOnUtc);
public sealed record PagedElderlyHelpRequests(IReadOnlyList<ElderlyHelpRequestResponse> Items, int Page, int PageSize, int TotalCount);
internal static class HelpRequestMap { public static ElderlyHelpRequestResponse Map(ElderlyHelpRequest x, Guid elderlyId) => new(x.Id, elderlyId, x.Status, x.ActorKey, x.ActionKey, x.NeedKey, x.QualifierKey, x.CustomText, x.CreatedOnUtc, x.UpdatedOnUtc); public static ElderlyHelpRequestHistoryResponse Map(ElderlyHelpRequestHistory x) => new(x.Id, x.Action, x.Status, x.Reason, x.ActorUserId.Value, x.OccurredOnUtc); }
internal static class HelpRequestErrors { public static readonly Error NotFound = new("Families.HelpRequest.NotFound", "The help request was not found."); public static readonly Error Conflict = new("Families.HelpRequest.IdempotencyConflict", "The idempotency key was already used for a different request."); public static readonly Error Invalid = new("Families.HelpRequest.InvalidOperation", "The help request is invalid."); }
public sealed class CreateElderlyHelpRequestCommandHandler(IFamiliesDbContext db, IHelpRequestCatalogGateway catalog, IHelpRequestNotificationGateway notifications) : ICommandHandler<CreateElderlyHelpRequestCommand, ElderlyHelpRequestResponse>
{
    public async Task<Result<ElderlyHelpRequestResponse>> Handle(CreateElderlyHelpRequestCommand r, CancellationToken ct)
    {
        var actorKey = r.ActorKey.Trim();
        var actionKey = r.ActionKey.Trim();
        var needKey = r.NeedKey.Trim();
        var qualifierKey = string.IsNullOrWhiteSpace(r.QualifierKey) ? null : r.QualifierKey.Trim();
        var customText = string.IsNullOrWhiteSpace(r.CustomText) ? null : r.CustomText.Trim();
        var idempotencyKey = r.IdempotencyKey.Trim();

        var elderly = await db.Elderlies.SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct))
            return HelpRequestErrors.NotFound;

        var existing = await db.ElderlyHelpRequests
            .Include(x => x.History)
            .SingleOrDefaultAsync(x => x.ElderlyIdentityUserId == r.ElderlyIdentityUserId && x.IdempotencyKey == idempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.ActorKey == actorKey && existing.ActionKey == actionKey && existing.NeedKey == needKey &&
                existing.QualifierKey == qualifierKey && existing.CustomText == customText)
                return HelpRequestMap.Map(existing, elderly.Id.Value);

            return HelpRequestErrors.Conflict;
        }

        var keys = new[] { actorKey, actionKey, needKey }
            .Concat(qualifierKey is null ? [] : [qualifierKey])
            .ToArray();
        var items = await catalog.GetActiveAsync(keys, ct);
        if (items.Count != keys.Length)
            return HelpRequestErrors.Invalid;

        var actor = items.SingleOrDefault(x => x.Key == actorKey && x.Category == SentenceBuilderCatalogCategory.Actor);
        var action = items.SingleOrDefault(x => x.Key == actionKey && x.Category == SentenceBuilderCatalogCategory.Action);
        var need = items.SingleOrDefault(x => x.Key == needKey && x.Category == SentenceBuilderCatalogCategory.Need);
        var qualifier = qualifierKey is null ? null : items.SingleOrDefault(x => x.Key == qualifierKey && x.Category == SentenceBuilderCatalogCategory.Qualifier);
        if (actor is null || action is null || need is null || (qualifierKey is not null && qualifier is null))
            return HelpRequestErrors.Invalid;

        try
        {
            var request = ElderlyHelpRequest.Create(
                r.ElderlyIdentityUserId,
                elderly.FamilyId.Value,
                actor.Key, actor.ArabicLabel, actor.EnglishLabel,
                action.Key, action.ArabicLabel, action.EnglishLabel,
                need.Key, need.ArabicLabel, need.EnglishLabel,
                qualifier?.Key, qualifier?.ArabicLabel, qualifier?.EnglishLabel,
                customText,
                idempotencyKey);
            db.ElderlyHelpRequests.Add(request);
            await db.SaveChangesAsync(ct);
            await notifications.NotifyCreatedAsync(r.ElderlyIdentityUserId, elderly.Id.Value, request.Id, ct);
            return HelpRequestMap.Map(request, elderly.Id.Value);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return HelpRequestErrors.Invalid;
        }
    }
}
public sealed class GetElderlyHelpRequestsQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetElderlyHelpRequestsQuery, IReadOnlyList<ElderlyHelpRequestResponse>>
{
    public async Task<Result<IReadOnlyList<ElderlyHelpRequestResponse>>> Handle(GetElderlyHelpRequestsQuery r, CancellationToken ct)
    {
        var e = await db.Elderlies.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (e is null || !await db.Families.AnyAsync(x => x.Id == e.FamilyId && x.DeletedOnUtc == null, ct)) return HelpRequestErrors.NotFound;
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var q = db.ElderlyHelpRequests.AsNoTracking().Where(x => x.ElderlyIdentityUserId == r.ElderlyIdentityUserId && x.CreatedOnUtc >= cutoff);
        if (r.RequestId.HasValue) q = q.Where(x => x.Id == r.RequestId);
        var rows = await q.OrderByDescending(x => x.CreatedOnUtc).ToListAsync(ct);
        return rows.Select(x => HelpRequestMap.Map(x, e.Id.Value)).ToList();
    }
}
public sealed class GetElderlyHelpRequestQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetElderlyHelpRequestQuery, ElderlyHelpRequestResponse>
{
    public async Task<Result<ElderlyHelpRequestResponse>> Handle(GetElderlyHelpRequestQuery r, CancellationToken ct)
    {
        var elderly = await db.Elderlies.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct))
            return HelpRequestErrors.NotFound;

        var cutoff = DateTime.UtcNow.AddYears(-1);
        var request = await db.ElderlyHelpRequests.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == r.RequestId && x.ElderlyIdentityUserId == r.ElderlyIdentityUserId && x.CreatedOnUtc >= cutoff, ct);
        return request is null ? HelpRequestErrors.NotFound : HelpRequestMap.Map(request, elderly.Id.Value);
    }
}
public sealed class CancelElderlyHelpRequestCommandHandler(IFamiliesDbContext db) : ICommandHandler<CancelElderlyHelpRequestCommand, ElderlyHelpRequestResponse>
{
    public async Task<Result<ElderlyHelpRequestResponse>> Handle(CancelElderlyHelpRequestCommand r, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var e = await db.Elderlies.SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (e is null || !await db.Families.AnyAsync(x => x.Id == e.FamilyId && x.DeletedOnUtc == null, ct)) return HelpRequestErrors.NotFound;
        var x = await db.ElderlyHelpRequests.Include(x => x.History)
            .SingleOrDefaultAsync(x => x.Id == r.RequestId && x.ElderlyIdentityUserId == r.ElderlyIdentityUserId && x.CreatedOnUtc >= cutoff, ct);
        if (x is null) return HelpRequestErrors.NotFound;
        try
        {
            x.Transition(ElderlyHelpRequestHistoryAction.Cancelled, r.Reason, r.ElderlyIdentityUserId);
            await db.SaveChangesAsync(ct);
            return HelpRequestMap.Map(x, e.Id.Value);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return HelpRequestErrors.Invalid;
        }
    }
}
public sealed class ChangeElderlyHelpRequestStatusCommandHandler(IFamiliesDbContext db) : ICommandHandler<ChangeElderlyHelpRequestStatusCommand, ElderlyHelpRequestResponse>
{
    public async Task<Result<ElderlyHelpRequestResponse>> Handle(ChangeElderlyHelpRequestStatusCommand r, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var request = await db.ElderlyHelpRequests.Include(x => x.History)
            .SingleOrDefaultAsync(x => x.Id == r.RequestId && x.CreatedOnUtc >= cutoff, ct);
        if (request is null) return HelpRequestErrors.NotFound;
        if (r.Action == ElderlyHelpRequestHistoryAction.Cancelled && string.IsNullOrWhiteSpace(r.Reason))
            return HelpRequestErrors.Invalid;
        var elderly = await db.Elderlies.AsNoTracking().SingleOrDefaultAsync(e => e.IdentityUserId == request.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(f => f.Id == elderly.FamilyId && f.DeletedOnUtc == null, ct)) return HelpRequestErrors.NotFound;
        try
        {
            request.Transition(r.Action, r.Reason, r.ActorUserId);
            await db.SaveChangesAsync(ct);
            return HelpRequestMap.Map(request, elderly.Id.Value);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException)
        {
            return HelpRequestErrors.Invalid;
        }
    }
}
internal static class AdminHelpRequestAccess { public static async Task Audit(IFamiliesDbContext db, UserId actor, string account, string action, Guid? resource, string correlation, CancellationToken ct) { db.AdminMedicationAccessAudits.Add(Domain.Medications.AdminMedicationAccessAudit.Create(actor, account, action, "HelpRequest", resource, DateTime.UtcNow, correlation)); await db.SaveChangesAsync(ct); } }
public sealed class AdminListElderlyHelpRequestsQueryHandler(IFamiliesDbContext db) : IQueryHandler<AdminListElderlyHelpRequestsQuery, PagedElderlyHelpRequests>
{
    public async Task<Result<PagedElderlyHelpRequests>> Handle(AdminListElderlyHelpRequestsQuery r, CancellationToken ct)
    {
        await AdminHelpRequestAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "ListHelpRequests", null, r.CorrelationId, ct);
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var q = db.ElderlyHelpRequests.AsNoTracking()
            .Join(db.Elderlies.AsNoTracking(), x => x.ElderlyIdentityUserId, e => e.IdentityUserId, (x, e) => new { x, e })
            .Where(x => x.x.CreatedOnUtc >= cutoff && db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null));
        if (r.Status.HasValue) q = q.Where(x => x.x.Status == r.Status);
        if (r.ElderlyId.HasValue) q = q.Where(x => x.e.Id.Value == r.ElderlyId);
        var total = await q.CountAsync(ct);
        var page = Math.Max(1, r.Page);
        var size = Math.Clamp(r.PageSize, 1, 100);
        var rows = await q.OrderByDescending(x => x.x.CreatedOnUtc).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedElderlyHelpRequests(rows.Select(x => HelpRequestMap.Map(x.x, x.e.Id.Value)).ToList(), page, size, total);
    }
}
public sealed class AdminGetElderlyHelpRequestQueryHandler(IFamiliesDbContext db) : IQueryHandler<AdminGetElderlyHelpRequestQuery, ElderlyHelpRequestResponse>
{
    public async Task<Result<ElderlyHelpRequestResponse>> Handle(AdminGetElderlyHelpRequestQuery r, CancellationToken ct)
    {
        await AdminHelpRequestAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "GetHelpRequest", r.RequestId, r.CorrelationId, ct);
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var x = await db.ElderlyHelpRequests.AsNoTracking()
            .Join(db.Elderlies.AsNoTracking(), x => x.ElderlyIdentityUserId, e => e.IdentityUserId, (x, e) => new { x, e })
            .Where(x => x.x.Id == r.RequestId && x.x.CreatedOnUtc >= cutoff && db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null))
            .SingleOrDefaultAsync(ct);
        return x is null ? HelpRequestErrors.NotFound : HelpRequestMap.Map(x.x, x.e.Id.Value);
    }
}
public sealed class AdminGetElderlyHelpRequestHistoryQueryHandler(IFamiliesDbContext db) : IQueryHandler<AdminGetElderlyHelpRequestHistoryQuery, IReadOnlyList<ElderlyHelpRequestHistoryResponse>>
{
    public async Task<Result<IReadOnlyList<ElderlyHelpRequestHistoryResponse>>> Handle(AdminGetElderlyHelpRequestHistoryQuery r, CancellationToken ct)
    {
        await AdminHelpRequestAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "GetHelpRequestHistory", r.RequestId, r.CorrelationId, ct);
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var valid = await db.ElderlyHelpRequests.AsNoTracking()
            .Join(db.Elderlies.AsNoTracking(), x => x.ElderlyIdentityUserId, e => e.IdentityUserId, (x, e) => new { x, e })
            .AnyAsync(x => x.x.Id == r.RequestId && x.x.CreatedOnUtc >= cutoff && db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null), ct);
        if (!valid) return HelpRequestErrors.NotFound;
        return await db.ElderlyHelpRequestHistories.AsNoTracking()
            .Where(x => x.HelpRequestId == r.RequestId && x.OccurredOnUtc >= cutoff)
            .OrderBy(x => x.OccurredOnUtc)
            .Select(x => new ElderlyHelpRequestHistoryResponse(x.Id, x.Action, x.Status, x.Reason, x.ActorUserId.Value, x.OccurredOnUtc))
            .ToListAsync(ct);
    }
}
public sealed class AdminGetElderlyHelpRequestAggregateQueryHandler(IFamiliesDbContext db) : IQueryHandler<AdminGetElderlyHelpRequestAggregateQuery, IReadOnlyDictionary<ElderlyHelpRequestStatus, int>>
{
    public async Task<Result<IReadOnlyDictionary<ElderlyHelpRequestStatus, int>>> Handle(AdminGetElderlyHelpRequestAggregateQuery r, CancellationToken ct)
    {
        await AdminHelpRequestAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "GetHelpRequestAggregate", null, r.CorrelationId, ct);
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var rows = await db.ElderlyHelpRequests.AsNoTracking()
            .Where(x => x.CreatedOnUtc >= cutoff && db.Elderlies.Any(e => e.IdentityUserId == x.ElderlyIdentityUserId && db.Families.Any(f => f.Id == e.FamilyId && f.DeletedOnUtc == null)))
            .GroupBy(x => x.Status)
            .Select(x => new { x.Key, Count = x.Count() })
            .ToListAsync(ct);
        return rows.ToDictionary(x => x.Key, x => x.Count);
    }
}
