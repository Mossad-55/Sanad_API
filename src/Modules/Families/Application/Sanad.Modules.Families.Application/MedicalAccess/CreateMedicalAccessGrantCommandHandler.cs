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
        var correlationId = Guid.NewGuid().ToString();

        // Resolve the family for the user
        var dependent = await MedicalAccessFamilyAuthorization.GetDependentAsync(
            dbContext, request.DependentId, request.GrantedByUserId, requireManagePermission: true, cancellationToken);
        if (dependent is null)
        {
            // Authorization denial audit (append-only). A failed audit write must
            // not be reported as a successfully audited denial.
            if (request.DependentId != ElderlyId.Empty)
            {
                try
                {
                    dbContext.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(
                        request.GrantedByUserId,
                        request.DependentId,
                        null,
                        null,
                        CaregiverMedicalAccessAuditAction.CreateGrant,
                        CaregiverMedicalAccessAuditDecision.Denied,
                        "Families.AccessDenied",
                        null,
                        null,
                        null,
                        null,
                        null,
                        DateTime.UtcNow,
                        correlationId));
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (DbUpdateException)
                {
                    return Result<MedicalAccessGrant>.Failure(
                        new Error("Families.AuditWriteFailed", "The medical access audit could not be recorded."));
                }
            }

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

        // Atomic audit event
        dbContext.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(
            request.GrantedByUserId,
            request.DependentId,
            request.GranteeUserId,
            grant.Id,
            CaregiverMedicalAccessAuditAction.CreateGrant,
            CaregiverMedicalAccessAuditDecision.Allowed,
            null,
            request.GrantType,
            request.CanViewRecords,
            request.CanEditRecords,
            request.CanShareWithOthers,
            request.ExpiresOnUtc,
            grant.GrantedOnUtc,
            correlationId));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            return Result<MedicalAccessGrant>.Failure(
                new Error("Families.AuditWriteFailed", "The medical access audit could not be recorded."));
        }

        return Result<MedicalAccessGrant>.Success(grant);
    }
}

public sealed class RevokeMedicalAccessGrantCommandHandler(
    IFamiliesDbContext dbContext) : ICommandHandler<RevokeMedicalAccessGrantCommand, bool>
{
    public async Task<Result<bool>> Handle(RevokeMedicalAccessGrantCommand request, CancellationToken cancellationToken)
    {
        var correlationId = Guid.NewGuid().ToString();

        var dependent = await MedicalAccessFamilyAuthorization.GetDependentAsync(
            dbContext, request.DependentId, request.RevokedByUserId, requireManagePermission: true, cancellationToken);
        if (dependent is null)
        {
            if (request.DependentId != ElderlyId.Empty)
            {
                try
                {
                    dbContext.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(
                        request.RevokedByUserId,
                        request.DependentId,
                        null,
                        null,
                        CaregiverMedicalAccessAuditAction.RevokeGrant,
                        CaregiverMedicalAccessAuditDecision.Denied,
                        "Families.AccessDenied",
                        null,
                        null,
                        null,
                        null,
                        null,
                        DateTime.UtcNow,
                        correlationId));
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (DbUpdateException)
                {
                    return Result<bool>.Failure(
                        new Error("Families.AuditWriteFailed", "The medical access audit could not be recorded."));
                }
            }

            return Result<bool>.Failure(new Error("Families.AccessDenied", "The dependent is not accessible to this family member."));
        }

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

        // Atomic audit event
        dbContext.CaregiverMedicalAccessAudits.Add(CaregiverMedicalAccessAudit.Create(
            request.RevokedByUserId,
            request.DependentId,
            grant.GranteeUserId,
            grant.Id,
            CaregiverMedicalAccessAuditAction.RevokeGrant,
            CaregiverMedicalAccessAuditDecision.Allowed,
            null,
            grant.GrantType,
            grant.CanViewRecords,
            grant.CanEditRecords,
            grant.CanShareWithOthers,
            grant.ExpiresOnUtc,
            DateTime.UtcNow,
            correlationId));

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (DbUpdateException)
        {
            return Result<bool>.Failure(
                new Error("Families.AuditWriteFailed", "The medical access audit could not be recorded."));
        }
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