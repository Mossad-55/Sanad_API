using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Families.Application.Elderlies;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Families.Application.HelpRequests;

public sealed record GetFamilyDependentHelpRequestQuery(
    UserId UserId,
    ElderlyId DependentId,
    Guid RequestId) : IQuery<FamilyDependentHelpRequestResponse>;

public sealed record FamilyDependentHelpRequestResponse(
    Guid Id,
    string ElderlyName,
    string Status,
    string ActorKey,
    string ActionKey,
    string NeedKey,
    string? QualifierKey,
    string? CustomText,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed class GetFamilyDependentHelpRequestQueryHandler(
    IFamiliesDbContext dbContext,
    IFamilyIdentityGateway identityGateway) : IQueryHandler<
    GetFamilyDependentHelpRequestQuery,
    FamilyDependentHelpRequestResponse>
{
    public async Task<Result<FamilyDependentHelpRequestResponse>> Handle(
        GetFamilyDependentHelpRequestQuery request,
        CancellationToken cancellationToken)
    {
        _ = identityGateway;
        // Resolve the family for the user
        var family = await FamilyAccess.ResolveFamilyAsync(
            dbContext,
            request.UserId,
            cancellationToken);

        if (family is null)
        {
            return Result<FamilyDependentHelpRequestResponse>.Failure(
                ElderlyErrors.FamilyNotFound);
        }

        // Verify the dependent belongs to this family
        var elderly = await dbContext.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                e => e.Id == request.DependentId &&
                     e.FamilyId == family.Id,
                cancellationToken);

        if (elderly is null)
        {
            return Result<FamilyDependentHelpRequestResponse>.Failure(
                ElderlyErrors.NotFound);
        }

        // Get the help request (only from last year for performance)
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var helpRequest = await dbContext.ElderlyHelpRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                h => h.Id == request.RequestId &&
                     h.ElderlyIdentityUserId == elderly.IdentityUserId &&
                     h.CreatedOnUtc >= cutoff,
                cancellationToken);

        if (helpRequest is null)
        {
            return Result<FamilyDependentHelpRequestResponse>.Failure(
                HelpRequestErrors.NotFound);
        }

        return Result<FamilyDependentHelpRequestResponse>.Success(
            new FamilyDependentHelpRequestResponse(
                helpRequest.Id,
 elderly.IdentityUserId.Value.ToString(),
                helpRequest.Status.ToString(),
                helpRequest.ActorKey,
                helpRequest.ActionKey,
                helpRequest.NeedKey,
                helpRequest.QualifierKey,
                helpRequest.CustomText,
                helpRequest.CreatedOnUtc,
                helpRequest.UpdatedOnUtc));
    }
}
