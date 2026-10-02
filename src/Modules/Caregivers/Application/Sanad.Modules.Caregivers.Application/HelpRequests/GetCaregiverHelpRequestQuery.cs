using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.Families;
using Sanad.Modules.Families.Domain.HelpRequests;

namespace Sanad.Modules.Caregivers.Application.HelpRequests;

public sealed record CaregiverHelpRequestResponse(
    Guid Id,
    Guid ElderlyId,
    string ActorKey,
    string ActionKey,
    string NeedKey,
    string? QualifierKey,
    string? CustomText,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

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
                c => c.UserId == request.UserId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                CaregiverErrors.NotFound);
        }

        // Get confirmed/in-progress bookings for this caregiver to determine which elderly people they can see help requests for
        var bookings = await dbContext.Bookings
            .AsNoTracking()
            .Where(b => b.CaregiverId == caregiver.Id &&
                        (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.InProgress))
            .Select(b => b.ElderlyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (bookings.Count == 0)
        {
            // No active bookings, caregiver cannot see any help requests
            return Result<CaregiverHelpRequestResponse>.Failure(
                CaregiverErrors.NotFound);
        }

        // Get the help request (only from last year for performance)
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var helpRequest = await familiesDb.ElderlyHelpRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(
                h => h.Id == request.RequestId &&
                     bookings.Contains(h.ElderlyIdentityUserId) &&
                     h.CreatedOnUtc >= cutoff,
                cancellationToken);

        if (helpRequest is null)
        {
            return Result<CaregiverHelpRequestResponse>.Failure(
                HelpRequestErrors.NotFound);
        }

        return Result<CaregiverHelpRequestResponse>.Success(
            new CaregiverHelpRequestResponse(
                helpRequest.Id,
                helpRequest.ElderlyIdentityUserId.Value,
                helpRequest.ActorKey,
                helpRequest.ActionKey,
                helpRequest.NeedKey,
                helpRequest.QualifierKey,
                helpRequest.CustomText,
                helpRequest.CreatedOnUtc,
                helpRequest.UpdatedOnUtc));
    }
}