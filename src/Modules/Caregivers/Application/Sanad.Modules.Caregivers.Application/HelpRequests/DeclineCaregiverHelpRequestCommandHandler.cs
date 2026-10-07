using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.HelpRequests;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.HelpRequests;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Caregivers.Application.HelpRequests;
public sealed class DeclineCaregiverHelpRequestCommandHandler(
    ICaregiversDbContext dbContext,
    IFamiliesDbContext familiesDb) : ICommandHandler<DeclineCaregiverHelpRequestCommand, CaregiverHelpRequestResponse>
{
    private static readonly Error InvalidReason = new(
        "Caregivers.HelpRequest.InvalidReason",
        "A reason of at most 500 characters is required.");

    public async Task<Result<CaregiverHelpRequestResponse>> Handle(
        DeclineCaregiverHelpRequestCommand request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Trim().Length > 500)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(InvalidReason);
        }

        // Get the caregiver profile for the user
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.UserId == request.UserId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                CaregiverErrors.NotFound);
        }

        // Get confirmed/in-progress bookings for this caregiver to determine which elderly people they can see help requests for
        var bookingElderlyIds = await familiesDb.Bookings
            .AsNoTracking()
            .Where(b => b.CaregiverId == caregiver.Id &&
                        (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.InProgress))
            .Select(b => b.ElderlyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (bookingElderlyIds.Count == 0)
        {
            // No active bookings, caregiver cannot see any help requests
            return Result<CaregiverHelpRequestResponse>.Failure(
                CaregiverErrors.NotFound);
        }

        // Get the help request with elderly info (only from last year for performance)
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var helpRequestQuery = familiesDb.ElderlyHelpRequests
            .AsNoTracking()
            .Join(
                familiesDb.Elderlies.AsNoTracking(),
                h => h.ElderlyIdentityUserId,
                e => e.IdentityUserId,
                (h, e) => new { HelpRequest = h, Elderly = e })
            .Where(x =>
                x.HelpRequest.Id == request.RequestId &&
                bookingElderlyIds.Contains(x.Elderly.Id) &&
                x.HelpRequest.CreatedOnUtc >= cutoff);

        // Get the elderly entity for this help request
        var elderly = await helpRequestQuery
            .Select(x => x.Elderly)
            .FirstOrDefaultAsync(cancellationToken);

        if (elderly is null)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                new Error("Caregivers.HelpRequest.NotFound", "The help request was not found."));
        }

        var helpRequest = await familiesDb.ElderlyHelpRequests
            .AsTracking()
            .SingleOrDefaultAsync(item => item.Id == request.RequestId, cancellationToken);

        if (helpRequest is null)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                new Error("Caregivers.HelpRequest.NotFound", "The help request was not found."));
        }

        // Check if the help request can be declined (should be in Pending status)
        if (helpRequest.Status != ElderlyHelpRequestStatus.Pending)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                new Error("Caregivers.HelpRequest.InvalidOperation", "The help request cannot be declined in its current state."));
        }

        // Decline the help request
        helpRequest.Transition(ElderlyHelpRequestHistoryAction.Rejected, request.Reason, request.UserId);
        familiesDb.ElderlyHelpRequestHistories.Add(helpRequest.History.Last());
        await familiesDb.SaveChangesAsync(cancellationToken);

        return Result<CaregiverHelpRequestResponse>.Success(
            new CaregiverHelpRequestResponse(
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
