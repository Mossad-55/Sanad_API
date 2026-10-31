using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Families.Application.Abstractions.Data;
using Ahlena.Modules.Families.Application.Families;
using Ahlena.Modules.Families.Domain.Elderlies;

namespace Ahlena.Modules.Families.Application.MedicalAccess;

public sealed record MedicalAccessGrantResponse(
    Guid Id,
    Guid DependentId,
    Guid GranteeUserId,
    string GrantType,
    bool CanViewRecords,
    bool CanEditRecords,
    bool CanShareWithOthers,
    DateTime GrantedOnUtc,
    DateTime? ExpiresOnUtc,
    string? Notes,
    DateTime? RevokedOnUtc,
    bool IsActive);

public sealed record GetMedicalAccessGrantsQuery(
    Guid DependentId,
    UserId ActorUserId,
    Guid? GrantId = null) : IQuery<MedicalAccessGrantResponse[]>;

public sealed class GetMedicalAccessGrantsQueryHandler(
    IFamiliesDbContext dbContext) : IQueryHandler<
    GetMedicalAccessGrantsQuery,
    MedicalAccessGrantResponse[]>
{
    public async Task<Result<MedicalAccessGrantResponse[]>> Handle(
        GetMedicalAccessGrantsQuery request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(dbContext, request.ActorUserId, cancellationToken);
        if (family is null || !FamilyAccess.IsMember(family, request.ActorUserId))
        {
            return Result<MedicalAccessGrantResponse[]>.Failure(
                new Error("Families.AccessDenied", "The dependent is not accessible to this family member."));
        }

        var dependentId = new ElderlyId(request.DependentId);
        var ownsDependent = await dbContext.Elderlies.AsNoTracking().AnyAsync(
            e => e.Id == dependentId && e.FamilyId == family.Id, cancellationToken);
        if (!ownsDependent)
        {
            return Result<MedicalAccessGrantResponse[]>.Failure(
                new Error("Families.AccessDenied", "The dependent is not accessible to this family member."));
        }

        var query = dbContext.MedicalAccessGrants.AsNoTracking()
            .Where(g => g.DependentId == dependentId);

        if (request.GrantId.HasValue)
        {
            query = query.Where(g => g.Id == new MedicalAccessGrantId(request.GrantId.Value));
        }

        var entities = await query.ToArrayAsync(cancellationToken);
        var utcNow = DateTime.UtcNow;
        var grants = entities.Select(g => new MedicalAccessGrantResponse(
                g.Id.Value,
                g.DependentId.Value,
                g.GranteeUserId.Value,
                g.GrantType.ToString(),
                g.CanViewRecords,
                g.CanEditRecords,
                g.CanShareWithOthers,
                g.GrantedOnUtc,
                g.ExpiresOnUtc,
                g.Notes,
                g.RevokedOnUtc,
                g.RevokedOnUtc is null && (!g.ExpiresOnUtc.HasValue || g.ExpiresOnUtc.Value > utcNow)))
            .ToArray();

        return Result<MedicalAccessGrantResponse[]>.Success(grants);
    }
}
