using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.HelpRequests;

namespace Sanad.Modules.Families.Application.HelpRequests;

public sealed record FamilyDependentHelpRequestResponse(
    Guid Id,
    Guid ElderlyId,
    string ActorKey,
    string ActionKey,
    string NeedKey,
    string? QualifierKey,
    string? CustomText,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed record PagedFamilyDependentHelpRequests(
    IReadOnlyList<FamilyDependentHelpRequestResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetFamilyDependentHelpRequestsQuery(
    UserId UserId,
    ElderlyId DependentId,
    int Page = 1,
    int PageSize = 20,
    string? ActorKey = null,
    string? ActionKey = null,
    string? NeedKey = null,
    string? QualifierKey = null,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null) : IQuery<PagedFamilyDependentHelpRequests>;

public sealed class GetFamilyDependentHelpRequestsQueryHandler(
    IFamiliesDbContext dbContext,
    IFamilyIdentityGateway identityGateway) : IQueryHandler<
    GetFamilyDependentHelpRequestsQuery,
    PagedFamilyDependentHelpRequests>
{
    private static readonly Error InvalidDateRange = new(
        "Families.HelpRequest.InvalidDateRange",
        "The help request range must be an inclusive range of 31 days or fewer.");

    public async Task<Result<PagedFamilyDependentHelpRequests>> Handle(
        GetFamilyDependentHelpRequestsQuery request,
        CancellationToken cancellationToken)
    {
        // Validate date range if provided
        if (request.StartDate.HasValue && request.EndDate.HasValue &&
            request.EndDate.Value.DayNumber - request.StartDate.Value.DayNumber >= 31)
        {
            return Result<PagedFamilyDependentHelpRequests>.Failure(InvalidDateRange);
        }

        // Resolve the family for the user
        var family = await FamilyAccess.ResolveFamilyAsync(
            dbContext,
            request.UserId,
            cancellationToken);

        if (family is null)
        {
            return Result<PagedFamilyDependentHelpRequests>.Failure(
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
            return Result<PagedFamilyDependentHelpRequests>.Failure(
                ElderlyErrors.NotFound);
        }

        // Build query
        var query = dbContext.ElderlyHelpRequests
            .AsNoTracking()
            .Where(c => c.ElderlyIdentityUserId == elderly.IdentityUserId);

        // Apply filters
        if (!string.IsNullOrWhiteSpace(request.ActorKey))
            query = query.Where(c => c.ActorKey == request.ActorKey.Trim());

        if (!string.IsNullOrWhiteSpace(request.ActionKey))
            query = query.Where(c => c.ActionKey == request.ActionKey.Trim());

        if (!string.IsNullOrWhiteSpace(request.NeedKey))
            query = query.Where(c => c.NeedKey == request.NeedKey.Trim());

        if (!string.IsNullOrWhiteSpace(request.QualifierKey))
            query = query.Where(c => c.QualifierKey == request.QualifierKey.Trim());

        if (request.StartDate.HasValue)
            query = query.Where(c => c.CreatedOnUtc >= request.StartDate.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));

        if (request.EndDate.HasValue)
            query = query.Where(c => c.CreatedOnUtc <= request.EndDate.Value.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc));

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);

        var helpRequests = await query
            .OrderByDescending(c => c.CreatedOnUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(c => new FamilyDependentHelpRequestResponse(
                c.Id,
                c.ElderlyIdentityUserId.Value,
                c.ActorKey,
                c.ActionKey,
                c.NeedKey,
                c.QualifierKey,
                c.CustomText,
                c.CreatedOnUtc,
                c.UpdatedOnUtc))
            .ToListAsync(cancellationToken);

        return Result<PagedFamilyDependentHelpRequests>.Success(
            new PagedFamilyDependentHelpRequests(helpRequests, page, size, totalCount));
    }
}