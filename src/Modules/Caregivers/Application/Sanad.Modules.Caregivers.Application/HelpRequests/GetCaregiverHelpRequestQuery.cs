using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.HelpRequests;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.HelpRequests;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Caregivers.Application.HelpRequests;

public sealed record GetCaregiverHelpRequestQuery(
    UserId UserId,
    Guid RequestId) : IQuery<CaregiverHelpRequestResponse>;

public sealed class GetCaregiverHelpRequestQueryHandler(
    ICaregiversDbContext dbContext,
    IFamiliesDbContext familiesDb,
    IFamilyIdentityGateway familiesGateway) : IQueryHandler<
    GetCaregiverHelpRequestQuery,
    CaregiverHelpRequestResponse>
{
    public async Task<Result<CaregiverHelpRequestResponse>> Handle(
        GetCaregiverHelpRequestQuery request,
        CancellationToken cancellationToken)
    {
        // Get the caregiver profile for the user
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.UserId.Value),
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

        var helpRequest = await helpRequestQuery.Select(x => x.HelpRequest)
            .FirstOrDefaultAsync(cancellationToken);

        if (helpRequest is null)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                new Error("Families.HelpRequest.NotFound", "The help request was not found."));
        }

        // Get the elderly entity for this help request
        var elderly = await helpRequestQuery
            .Select(x => x.Elderly)
            .FirstOrDefaultAsync(cancellationToken);

        if (elderly is null)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                new Error("Families.HelpRequest.NotFound", "The help request was not found."));
        }

        return Result<CaregiverHelpRequestResponse>.Success(
            new CaregiverHelpRequestResponse(
                helpRequest.Id,
                elderly.Id.Value,
                helpRequest.ActorKey,
                helpRequest.ActionKey,
                helpRequest.NeedKey,
                helpRequest.QualifierKey,
                helpRequest.CustomText,
                helpRequest.CreatedOnUtc,
                helpRequest.UpdatedOnUtc));
    }
}