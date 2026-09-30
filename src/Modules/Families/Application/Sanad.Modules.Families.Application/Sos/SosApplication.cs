using MediatR;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Sos;
using Sanad.Modules.Families.Domain.Medications;
using Sanad.Modules.Families.Domain.Sos;

namespace Sanad.Modules.Families.Application.Sos;

public sealed record CreateElderlySosCommand(UserId ElderlyIdentityUserId, string IdempotencyKey, bool LocationConsentGranted, decimal? Latitude, decimal? Longitude) : ICommand<ElderlySosResponse>;
public sealed record CancelElderlySosCommand(UserId ElderlyIdentityUserId, Guid SosId) : ICommand<ElderlySosResponse>;
public sealed record ChangeElderlySosStatusCommand(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid SosId, ElderlySosHistoryAction Action) : ICommand<ElderlySosResponse>;
public sealed record GetElderlySosQuery(UserId ElderlyIdentityUserId, Guid? SosId = null) : IQuery<IReadOnlyList<ElderlySosResponse>>;
public sealed record GetElderlySosDetailQuery(UserId ElderlyIdentityUserId, Guid SosId) : IQuery<ElderlySosResponse>;
public sealed record AdminListElderlySosQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, ElderlySosStatus? Status = null, Guid? ElderlyId = null, int Page = 1, int PageSize = 20) : IQuery<PagedElderlySos>;
public sealed record AdminGetElderlySosQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid SosId) : IQuery<ElderlySosResponse>;
public sealed record AdminGetElderlySosHistoryQuery(UserId ActorUserId, string ActorAccountType, string CorrelationId, Guid SosId) : IQuery<IReadOnlyList<ElderlySosHistoryResponse>>;
public sealed record ElderlySosResponse(Guid Id, Guid ElderlyId, ElderlySosStatus Status, bool LocationConsentGranted, decimal? Latitude, decimal? Longitude, DateTime CreatedOnUtc, DateTime UpdatedOnUtc);
public sealed record ElderlySosHistoryResponse(Guid Id, ElderlySosHistoryAction Action, ElderlySosStatus Status, Guid ActorUserId, DateTime OccurredOnUtc);
public sealed record PagedElderlySos(IReadOnlyList<ElderlySosResponse> Items, int Page, int PageSize, int TotalCount);

internal static class SosErrors
{
    public static readonly Error NotFound = new("Families.Sos.NotFound", "The SOS event was not found.");
    public static readonly Error Conflict = new("Families.Sos.IdempotencyConflict", "The idempotency key was already used for a different SOS event.");
    public static readonly Error Invalid = new("Families.Sos.InvalidOperation", "The SOS operation is invalid.");
    public static readonly Error InvalidLocation = new("Families.Sos.InvalidLocation", "The SOS location is invalid.");
}

internal static class SosMap
{
    public static ElderlySosResponse Map(ElderlySos x, Guid elderlyId)
    {
        var visible = x.CreatedOnUtc >= DateTime.UtcNow.AddDays(-30);
        return new(x.Id, elderlyId, x.Status, x.LocationConsentGranted, visible ? x.Latitude : null, visible ? x.Longitude : null, x.CreatedOnUtc, x.UpdatedOnUtc);
    }
}

public sealed class CreateElderlySosCommandHandler(IFamiliesDbContext db, ISosNotificationGateway notifications) : ICommandHandler<CreateElderlySosCommand, ElderlySosResponse>
{
    public async Task<Result<ElderlySosResponse>> Handle(CreateElderlySosCommand r, CancellationToken ct)
    {
        if ((r.Latitude is null) != (r.Longitude is null) || (r.Latitude is not null && (!r.LocationConsentGranted || r.Latitude is < -90 or > 90 || r.Longitude is < -180 or > 180))) return SosErrors.InvalidLocation;
        var elderly = await db.Elderlies.SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct)) return SosErrors.NotFound;
        var key = r.IdempotencyKey.Trim();
        if (string.IsNullOrWhiteSpace(key)) return SosErrors.Invalid;
        var existing = await db.ElderlySos.Include(x => x.History).SingleOrDefaultAsync(x => x.ElderlyIdentityUserId == r.ElderlyIdentityUserId && x.IdempotencyKey == key, ct);
        if (existing is not null)
        {
            if (existing.LocationConsentGranted == r.LocationConsentGranted && existing.Latitude == Rounded(r.Latitude) && existing.Longitude == Rounded(r.Longitude))
            {
                await notifications.NotifyCreatedAsync(r.ElderlyIdentityUserId, elderly.Id.Value, existing.Id, ct);
                return SosMap.Map(existing, elderly.Id.Value);
            }
            return SosErrors.Conflict;
        }
        try
        {
            var sos = ElderlySos.Create(r.ElderlyIdentityUserId, elderly.FamilyId.Value, key, r.LocationConsentGranted, r.Latitude, r.Longitude);
            db.ElderlySos.Add(sos); await db.SaveChangesAsync(ct);
            await notifications.NotifyCreatedAsync(r.ElderlyIdentityUserId, elderly.Id.Value, sos.Id, ct);
            return SosMap.Map(sos, elderly.Id.Value);
        }
        catch (DbUpdateException)
        {
            if (db is DbContext context)
            {
                foreach (var entry in context.ChangeTracker.Entries<ElderlySos>().Where(x => x.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }

            var raced = await db.ElderlySos.Include(x => x.History)
                .SingleOrDefaultAsync(x => x.ElderlyIdentityUserId == r.ElderlyIdentityUserId && x.IdempotencyKey == key, ct);
            if (raced is null) throw;
            if (raced.LocationConsentGranted != r.LocationConsentGranted || raced.Latitude != Rounded(r.Latitude) || raced.Longitude != Rounded(r.Longitude)) return SosErrors.Conflict;
            await notifications.NotifyCreatedAsync(r.ElderlyIdentityUserId, elderly.Id.Value, raced.Id, ct);
            return SosMap.Map(raced, elderly.Id.Value);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException) { return SosErrors.Invalid; }
    }
    private static decimal? Rounded(decimal? value) => value is null ? null : decimal.Round(value.Value, 3);
}

public sealed class GetElderlySosQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetElderlySosQuery, IReadOnlyList<ElderlySosResponse>>
{
    public async Task<Result<IReadOnlyList<ElderlySosResponse>>> Handle(GetElderlySosQuery r, CancellationToken ct)
    {
        var elderly = await db.Elderlies.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct)) return SosErrors.NotFound;
        var q = db.ElderlySos.AsNoTracking().Where(x => x.ElderlyIdentityUserId == r.ElderlyIdentityUserId);
        if (r.SosId.HasValue) q = q.Where(x => x.Id == r.SosId.Value);
        var rows = await q.OrderByDescending(x => x.CreatedOnUtc).ToListAsync(ct);
        return rows.Select(x => SosMap.Map(x, elderly.Id.Value)).ToList();
    }
}

public sealed class GetElderlySosDetailQueryHandler(IFamiliesDbContext db) : IQueryHandler<GetElderlySosDetailQuery, ElderlySosResponse>
{
    public async Task<Result<ElderlySosResponse>> Handle(GetElderlySosDetailQuery r, CancellationToken ct)
    {
        var elderly = await db.Elderlies.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct)) return SosErrors.NotFound;
        var sos = await db.ElderlySos.AsNoTracking().SingleOrDefaultAsync(x => x.Id == r.SosId && x.ElderlyIdentityUserId == r.ElderlyIdentityUserId, ct);
        return sos is null ? SosErrors.NotFound : SosMap.Map(sos, elderly.Id.Value);
    }
}

public sealed class CancelElderlySosCommandHandler(IFamiliesDbContext db) : ICommandHandler<CancelElderlySosCommand, ElderlySosResponse>
{
    public async Task<Result<ElderlySosResponse>> Handle(CancelElderlySosCommand r, CancellationToken ct)
    {
        var elderly = await db.Elderlies.SingleOrDefaultAsync(x => x.IdentityUserId == r.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct)) return SosErrors.NotFound;
        var sos = await db.ElderlySos.Include(x => x.History).SingleOrDefaultAsync(x => x.Id == r.SosId && x.ElderlyIdentityUserId == r.ElderlyIdentityUserId, ct);
        if (sos is null) return SosErrors.NotFound;
        try { sos.Transition(ElderlySosHistoryAction.Cancelled, r.ElderlyIdentityUserId); await db.SaveChangesAsync(ct); return SosMap.Map(sos, elderly.Id.Value); }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException) { return SosErrors.Invalid; }
    }
}

public sealed class ChangeElderlySosStatusCommandHandler(IFamiliesDbContext db) : ICommandHandler<ChangeElderlySosStatusCommand, ElderlySosResponse>
{
    public async Task<Result<ElderlySosResponse>> Handle(ChangeElderlySosStatusCommand r, CancellationToken ct)
    {
        var sos = await db.ElderlySos.Include(x => x.History).SingleOrDefaultAsync(x => x.Id == r.SosId, ct);
        if (sos is null) return SosErrors.NotFound;
        var elderly = await db.Elderlies.AsNoTracking().SingleOrDefaultAsync(x => x.IdentityUserId == sos.ElderlyIdentityUserId, ct);
        if (elderly is null || !await db.Families.AnyAsync(x => x.Id == elderly.FamilyId && x.DeletedOnUtc == null, ct)) return SosErrors.NotFound;
        await SosAdminAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "ChangeSosStatus", r.SosId, r.CorrelationId, ct);
        try { sos.Transition(r.Action, r.ActorUserId); await db.SaveChangesAsync(ct); return SosMap.Map(sos, elderly.Id.Value); }
        catch (DbUpdateConcurrencyException) { return SosErrors.Invalid; }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException) { return SosErrors.Invalid; }
    }
}

internal static class SosAdminAccess
{
    public static async Task Audit(IFamiliesDbContext db, UserId actor, string account, string action, Guid? resource, string correlation, CancellationToken ct)
    { db.AdminMedicationAccessAudits.Add(AdminMedicationAccessAudit.Create(actor, account, action, "Sos", resource, DateTime.UtcNow, correlation)); await db.SaveChangesAsync(ct); }
}

public sealed class AdminListElderlySosQueryHandler(IFamiliesDbContext db) : IQueryHandler<AdminListElderlySosQuery, PagedElderlySos>
{
    public async Task<Result<PagedElderlySos>> Handle(AdminListElderlySosQuery r, CancellationToken ct)
    {
        await SosAdminAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "ListSos", null, r.CorrelationId, ct);
        var q = db.ElderlySos.AsNoTracking().Join(db.Elderlies.AsNoTracking(), x => x.ElderlyIdentityUserId, e => e.IdentityUserId, (x, e) => new { x, e }).Where(x => db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null));
        if (r.Status.HasValue) q = q.Where(x => x.x.Status == r.Status);
        if (r.ElderlyId.HasValue) q = q.Where(x => x.e.Id.Value == r.ElderlyId);
        var total = await q.CountAsync(ct); var page = Math.Max(1, r.Page); var size = Math.Clamp(r.PageSize, 1, 100);
        var rows = await q.OrderByDescending(x => x.x.CreatedOnUtc).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedElderlySos(rows.Select(x => SosMap.Map(x.x, x.e.Id.Value)).ToList(), page, size, total);
    }
}

public sealed class AdminGetElderlySosQueryHandler(IFamiliesDbContext db) : IQueryHandler<AdminGetElderlySosQuery, ElderlySosResponse>
{
    public async Task<Result<ElderlySosResponse>> Handle(AdminGetElderlySosQuery r, CancellationToken ct)
    {
        await SosAdminAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "GetSos", r.SosId, r.CorrelationId, ct);
        var x = await db.ElderlySos.AsNoTracking().Join(db.Elderlies.AsNoTracking(), s => s.ElderlyIdentityUserId, e => e.IdentityUserId, (s, e) => new { s, e }).Where(x => x.s.Id == r.SosId && db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null)).SingleOrDefaultAsync(ct);
        return x is null ? SosErrors.NotFound : SosMap.Map(x.s, x.e.Id.Value);
    }
}

public sealed class AdminGetElderlySosHistoryQueryHandler(IFamiliesDbContext db) : IQueryHandler<AdminGetElderlySosHistoryQuery, IReadOnlyList<ElderlySosHistoryResponse>>
{
    public async Task<Result<IReadOnlyList<ElderlySosHistoryResponse>>> Handle(AdminGetElderlySosHistoryQuery r, CancellationToken ct)
    {
        await SosAdminAccess.Audit(db, r.ActorUserId, r.ActorAccountType, "GetSosHistory", r.SosId, r.CorrelationId, ct);
        var valid = await db.ElderlySos.AsNoTracking().Join(db.Elderlies.AsNoTracking(), s => s.ElderlyIdentityUserId, e => e.IdentityUserId, (s, e) => new { s, e }).AnyAsync(x => x.s.Id == r.SosId && db.Families.Any(f => f.Id == x.e.FamilyId && f.DeletedOnUtc == null), ct);
        if (!valid) return SosErrors.NotFound;
        return await db.ElderlySosHistories.AsNoTracking().Where(x => x.ElderlySosId == r.SosId).OrderBy(x => x.OccurredOnUtc).Select(x => new ElderlySosHistoryResponse(x.Id, x.Action, x.Status, x.ActorUserId.Value, x.OccurredOnUtc)).ToListAsync(ct);
    }
}
