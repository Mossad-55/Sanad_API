using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Application.HelpRequests;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Bookings;
using Sanad.Modules.Families.Domain.Elderlies;
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

public sealed record PagedCaregiverHelpRequests(
    IReadOnlyList<CaregiverHelpRequestResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetCaregiverHelpRequestsQuery(
    UserId UserId,
    int Page = 1,
    int PageSize = 20,
    ElderlyHelpRequestStatus? Status = null,
    Guid? ElderlyId = null) : IQuery<PagedCaregiverHelpRequests>;

public sealed class GetCaregiverHelpRequestsQueryHandler(
    ICaregiversDbContext dbContext,
    IFamiliesDbContext familiesDb,
    IFamilyIdentityGateway familiesGateway) : IQueryHandler<
    GetCaregiverHelpRequestsQuery,
    PagedCaregiverHelpRequests>
{
    private static readonly Error InvalidDateRange = new(
        "Caregivers.HelpRequest.InvalidDateRange",
        "The help request range must be an inclusive range of 31 days or fewer.");

    public async Task<Result<PagedCaregiverHelpRequests>> Handle(
        GetCaregiverHelpRequestsQuery request,
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
            return Result<PagedCaregiverHelpRequests>.Failure(
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
            // No active bookings, return empty result
            return Result<PagedCaregiverHelpRequests>.Success(
                new PagedCaregiverHelpRequests(new List<CaregiverHelpRequestResponse>(), request.Page, request.PageSize, 0));
        }

        // If specific elderly ID is requested, validate that the caregiver has a booking for them
        if (request.ElderlyId.HasValue)
        {
            var hasBooking = bookingElderlyIds.Contains(new ElderlyId(request.ElderlyId.Value));
            if (!hasBooking)
            {
                return Result<PagedCaregiverHelpRequests>.Failure(
                    CaregiverErrors.NotFound);
            }
        }

        // Build query for help requests from elderly people the caregiver has bookings with
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var query = familiesDb.ElderlyHelpRequests
            .AsNoTracking()
            .Join(
                familiesDb.Elderlies.AsNoTracking(),
                h => h.ElderlyIdentityUserId,
                e => e.IdentityUserId,
                (h, e) => new { HelpRequest = h, Elderly = e })
            .Where(x =>
                x.HelpRequest.CreatedOnUtc >= cutoff &&
                bookingElderlyIds.Contains(x.Elderly.Id));

        // Apply additional filters
        if (request.ElderlyId.HasValue)
        {
            query = query.Where(x => x.Elderly.Id == new ElderlyId(request.ElderlyId.Value));
        }

        if (request.Status.HasValue)
        {
            query = query.Where(x => x.HelpRequest.Status == request.Status.Value);
        }

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);

        // Get paginated results
        var rows = await query
            .OrderByDescending(x => x.HelpRequest.CreatedOnUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(cancellationToken);

        // Map to response objects
        var helpRequests = rows.Select(x => new CaregiverHelpRequestResponse(
            x.HelpRequest.Id,
            x.Elderly.Id.Value,
            x.HelpRequest.ActorKey,
            x.HelpRequest.ActionKey,
            x.HelpRequest.NeedKey,
            x.HelpRequest.QualifierKey,
            x.HelpRequest.CustomText,
            x.HelpRequest.CreatedOnUtc,
            x.HelpRequest.UpdatedOnUtc))
            .ToList();

        return Result<PagedCaregiverHelpRequests>.Success(
            new PagedCaregiverHelpRequests(helpRequests, page, size, totalCount));
    }
}