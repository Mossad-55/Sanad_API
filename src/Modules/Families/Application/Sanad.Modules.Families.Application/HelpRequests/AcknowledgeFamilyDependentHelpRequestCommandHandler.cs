using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.HelpRequests;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Families.Application.HelpRequests;
public sealed class AcknowledgeFamilyDependentHelpRequestCommandHandler(
    IFamiliesDbContext dbContext) : ICommandHandler<AcknowledgeFamilyDependentHelpRequestCommand, ElderlyHelpRequestResponse>
{
    public async Task<Result<ElderlyHelpRequestResponse>> Handle(
        AcknowledgeFamilyDependentHelpRequestCommand request,
        CancellationToken cancellationToken)
    {
        // Resolve the family for the user
        var family = await FamilyAccess.ResolveFamilyAsync(
            dbContext,
            request.UserId,
            cancellationToken);

        if (family is null)
        {
            return Result<ElderlyHelpRequestResponse>.Failure(
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
            return Result<ElderlyHelpRequestResponse>.Failure(
                ElderlyErrors.NotFound);
        }

        // Get the help request (only from last year for performance)
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var helpRequest = await dbContext.ElderlyHelpRequests
            .AsTracking()
            .SingleOrDefaultAsync(
                h => h.Id == request.RequestId &&
                     h.ElderlyIdentityUserId == elderly.IdentityUserId &&
                     h.CreatedOnUtc >= cutoff,
                cancellationToken);

        if (helpRequest is null)
        {
            return Result<ElderlyHelpRequestResponse>.Failure(
                HelpRequestErrors.NotFound);
        }

        // Check if the help request can be acknowledged (should be in Pending status)
        if (helpRequest.Status != ElderlyHelpRequestStatus.Pending)
        {
            return Result<ElderlyHelpRequestResponse>.Failure(
                new Error("Families.HelpRequest.InvalidOperation", "The help request cannot be acknowledged in its current state."));
        }

        // Acknowledge the help request
        helpRequest.Transition(ElderlyHelpRequestHistoryAction.Acknowledged, null, request.UserId);
        dbContext.ElderlyHelpRequestHistories.Add(helpRequest.History.Last());
        await dbContext.SaveChangesAsync(cancellationToken);

        return Result<ElderlyHelpRequestResponse>.Success(
            new ElderlyHelpRequestResponse(
                helpRequest.Id,
                elderly.Id.Value,
                helpRequest.Status,
                helpRequest.ActorKey,
                helpRequest.ActionKey,
                helpRequest.NeedKey,
                helpRequest.QualifierKey,
                helpRequest.CustomText,
                helpRequest.CreatedOnUtc,
                helpRequest.UpdatedOnUtc));
    }
}
