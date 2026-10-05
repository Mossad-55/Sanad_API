using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Elderlies;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Families.Application.MedicalAccess;

public sealed class CreateMedicalAccessGrantCommandHandler(
    IFamiliesDbContext dbContext,
    IMedicalCaregiverGateway medicalCaregiverGateway) : ICommandHandler<CreateMedicalAccessGrantCommand, MedicalAccessGrant>
    {
    public async Task<Result<MedicalAccessGrant>> Handle(
        CreateMedicalAccessGrantCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve the family for the user
        var dependent = await MedicalAccessFamilyAuthorization.GetDependentAsync(
            dbContext, request.DependentId, request.GrantedByUserId, requireManagePermission: true, cancellationToken);
        if (dependent is null)
        {
            return Result<MedicalAccessGrant>.Failure(
                new Error("Families.AccessDenied", "The dependent is not accessible to this family member."));
        }

        if (!await medicalCaregiverGateway.IsActiveMedicalCaregiverAsync(
                request.GranteeUserId, cancellationToken))
        {
            return Result<MedicalAccessGrant>.Failure(
                new Error("MedicalAccess.InvalidGrant", "The grantee must be an active medical caregiver."));
        }

        // Check if grant already exists
        var now = DateTime.UtcNow;
        var existingGrant = await dbContext.MedicalAccessGrants
            .AnyAsync(
                g => g.DependentId == request.DependentId &&
                     g.GranteeUserId == request.GranteeUserId &&
                     g.RevokedOnUtc == null &&
                     (!g.ExpiresOnUtc.HasValue || g.ExpiresOnUtc.Value > now),
                cancellationToken);

        if (existingGrant)
        {
            return Result<MedicalAccessGrant>.Failure(
                new Error("MedicalAccess.GrantExists", "A medical access grant already exists for this dependent."));
        }

        MedicalAccessGrant grant;
        try
        {
            grant = MedicalAccessGrant.Create(
                request.DependentId, request.GrantedByUserId, request.GranteeUserId, request.GrantType,
                request.CanViewRecords, request.CanEditRecords, request.CanShareWithOthers,
                DateTime.UtcNow, request.ExpiresOnUtc, request.Notes);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<MedicalAccessGrant>.Failure(new Error("MedicalAccess.InvalidGrant", exception.Message));
        }

        dbContext.MedicalAccessGrants.Add(grant);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<MedicalAccessGrant>.Success(grant);
    }
}

public sealed class RevokeMedicalAccessGrantCommandHandler(
    IFamiliesDbContext dbContext) : ICommandHandler<RevokeMedicalAccessGrantCommand, bool>
{
    public async Task<Result<bool>> Handle(RevokeMedicalAccessGrantCommand request, CancellationToken cancellationToken)
    {
        var dependent = await MedicalAccessFamilyAuthorization.GetDependentAsync(
            dbContext, request.DependentId, request.RevokedByUserId, requireManagePermission: true, cancellationToken);
        if (dependent is null)
            return Result<bool>.Failure(new Error("Families.AccessDenied", "The dependent is not accessible to this family member."));

        var grant = await dbContext.MedicalAccessGrants.SingleOrDefaultAsync(
            g => g.Id == request.GrantId && g.DependentId == dependent.Id, cancellationToken);
        if (grant is null)
            return Result<bool>.Failure(new Error("MedicalAccess.GrantNotFound", "Medical access grant not found."));

        try
        {
            grant.Revoke(request.RevokedByUserId, DateTime.UtcNow);
        }
        catch (Sanad.BuildingBlocks.Domain.Exceptions.DomainException exception)
        {
            return Result<bool>.Failure(new Error("MedicalAccess.InvalidGrant", exception.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return Result<bool>.Success(true);
    }
}

internal static class MedicalAccessFamilyAuthorization
{
    internal static async Task<Sanad.Modules.Families.Domain.Elderlies.Elderly?> GetDependentAsync(
        IFamiliesDbContext dbContext,
        ElderlyId dependentId,
        UserId actorUserId,
        bool requireManagePermission,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(dbContext, actorUserId, cancellationToken);
        if (family is null || (requireManagePermission
                ? !FamilyAccess.CanManage(family, actorUserId)
                : !FamilyAccess.IsMember(family, actorUserId))) return null;

        return await dbContext.Elderlies.SingleOrDefaultAsync(
            e => e.Id == dependentId && e.FamilyId == family.Id,
            cancellationToken);
    }
}
