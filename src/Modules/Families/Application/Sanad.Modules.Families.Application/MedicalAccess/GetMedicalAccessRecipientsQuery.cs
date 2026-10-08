using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Families.Application.Abstractions.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.Modules.Families.Application.MedicalAccess;

public sealed record MedicalAccessRecipientResponse(
    Guid CaregiverId,
    Guid UserId,
    string ArabicFullName,
    string EnglishFullName,
    string? AvatarUrl,
    string CaregiverType,
    Guid? SpecializationId,
    string? SpecializationArabicName,
    string? SpecializationEnglishName,
    bool HasActiveGrant);

public sealed record MedicalAccessRecipientsResult(
    IReadOnlyList<MedicalAccessRecipientResponse> Items,
    int Page,
    int PageSize,
    int TotalCount,
    int TotalPages);

public sealed record GetMedicalAccessRecipientsQuery(
    Guid DependentId,
    UserId ActorUserId,
    string? Search = null,
    int Page = 1,
    int PageSize = 20) : IQuery<MedicalAccessRecipientsResult>;

public sealed class GetMedicalAccessRecipientsQueryHandler(
    IFamiliesDbContext dbContext,
    IMedicalCaregiverGateway medicalCaregiverGateway) : IQueryHandler<
    GetMedicalAccessRecipientsQuery,
    MedicalAccessRecipientsResult>
{
    public async Task<Result<MedicalAccessRecipientsResult>> Handle(
        GetMedicalAccessRecipientsQuery request,
        CancellationToken cancellationToken)
    {
        var family = await FamilyAccess.ResolveFamilyAsync(dbContext, request.ActorUserId, cancellationToken);
        if (family is null || !FamilyAccess.IsMember(family, request.ActorUserId))
        {
            return Result<MedicalAccessRecipientsResult>.Failure(
                new Error("Families.AccessDenied", "The dependent is not accessible to this family member."));
        }

        var dependentId = new ElderlyId(request.DependentId);
        var ownsDependent = await dbContext.Elderlies.AsNoTracking().AnyAsync(
            e => e.Id == dependentId && e.FamilyId == family.Id, cancellationToken);
        if (!ownsDependent)
        {
            return Result<MedicalAccessRecipientsResult>.Failure(
                new Error("Families.AccessDenied", "The dependent is not accessible to this family member."));
        }

        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(request.PageSize, 1, 100);

        MedicalCaregiverRecipientPage candidates =
            await medicalCaregiverGateway.SearchActiveMedicalCaregiversAsync(
                request.Search,
                page,
                pageSize,
                cancellationToken);

        var utcNow = DateTime.UtcNow;
        List<UserId> grantedUserIds = await dbContext.MedicalAccessGrants.AsNoTracking()
            .Where(g => g.DependentId == dependentId &&
                        g.RevokedOnUtc == null &&
                        (!g.ExpiresOnUtc.HasValue || g.ExpiresOnUtc.Value > utcNow))
            .Select(g => g.GranteeUserId)
            .ToListAsync(cancellationToken);

        var items = candidates.Items
            .Select(c => new MedicalAccessRecipientResponse(
                c.CaregiverId,
                c.UserId,
                c.ArabicFullName,
                c.EnglishFullName,
                c.AvatarUrl,
                "Medical",
                c.SpecializationId,
                c.SpecializationArabicName,
                c.SpecializationEnglishName,
                grantedUserIds.Contains(new UserId(c.UserId))))
            .ToList();

        int totalPages = (int)Math.Ceiling(candidates.TotalCount / (double)pageSize);

        return Result<MedicalAccessRecipientsResult>.Success(
            new MedicalAccessRecipientsResult(items, page, pageSize, candidates.TotalCount, totalPages));
    }
}
