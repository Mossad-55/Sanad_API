using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;
using Sanad.Modules.Families.Application.Families;

namespace Sanad.Modules.Families.Application.CheckIns;

public sealed record FamilyDependentCheckInResponse(
    Guid Id,
    Guid ElderlyId,
    bool Answer,
    DateOnly LocalDate,
    TimeOnly AnsweredAtLocalTime,
    DateTime AnsweredOnUtc,
    string TimeZoneId);

public sealed record PagedFamilyDependentCheckIns(
    IReadOnlyList<FamilyDependentCheckInResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetFamilyDependentCheckInsQuery(
    UserId UserId,
    ElderlyId DependentId,
    int Page = 1,
    int PageSize = 20,
    DateOnly? StartDate = null,
    DateOnly? EndDate = null,
    bool? Answer = null) : IQuery<PagedFamilyDependentCheckIns>;

public sealed class GetFamilyDependentCheckInsQueryHandler(
    IFamiliesDbContext dbContext,
    IFamilyIdentityGateway identityGateway) : IQueryHandler<
    GetFamilyDependentCheckInsQuery,
    PagedFamilyDependentCheckIns>
{
    private static readonly Error InvalidTimeZone = new(
        "Families.ElderlyCheckIn.InvalidTimeZone",
        "The elderly profile has an invalid time zone.");
    private static readonly Error InvalidDateRange = new(
        "Families.ElderlyCheckIn.InvalidDateRange",
        "The check-in range must be an inclusive range of 31 days or fewer.");

    public async Task<Result<PagedFamilyDependentCheckIns>> Handle(
        GetFamilyDependentCheckInsQuery request,
        CancellationToken cancellationToken)
    {
        // Validate date range if provided
        if (request.StartDate.HasValue && request.EndDate.HasValue &&
            request.EndDate.Value.DayNumber - request.StartDate.Value.DayNumber >= 31)
        {
            return Result<PagedFamilyDependentCheckIns>.Failure(InvalidDateRange);
        }

        // Resolve the family for the user
        var family = await FamilyAccess.ResolveFamilyAsync(
            dbContext,
            request.UserId,
            cancellationToken);

        if (family is null)
        {
            return Result<PagedFamilyDependentCheckIns>.Failure(
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
            return Result<PagedFamilyDependentCheckIns>.Failure(
                ElderlyErrors.NotFound);
        }

        // Validate timezone
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById(
                ElderlyTimeZone.Normalize(elderly.TimeZoneId));
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return Result<PagedFamilyDependentCheckIns>.Failure(
                InvalidTimeZone);
        }

        // Build query
        var query = dbContext.ElderlyCheckIns
            .AsNoTracking()
            .Where(c => c.ElderlyId == elderly.Id);

        if (request.StartDate.HasValue)
            query = query.Where(c => c.LocalDate >= request.StartDate.Value);

        if (request.EndDate.HasValue)
            query = query.Where(c => c.LocalDate <= request.EndDate.Value);

        if (request.Answer.HasValue)
            query = query.Where(c => c.Answer == request.Answer.Value);

        // Get total count
        var totalCount = await query.CountAsync(cancellationToken);

        // Apply pagination
        var page = Math.Max(1, request.Page);
        var size = Math.Clamp(request.PageSize, 1, 100);

        var checkIns = await query
            .OrderByDescending(c => c.LocalDate)
            .ThenByDescending(c => c.AnsweredOnUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(c => new FamilyDependentCheckInResponse(
                c.Id,
                c.ElderlyId.Value,
                c.Answer,
                c.LocalDate,
                c.AnsweredAtLocalTime,
                c.AnsweredOnUtc,
                elderly.TimeZoneId))
            .ToListAsync(cancellationToken);

        return Result<PagedFamilyDependentCheckIns>.Success(
            new PagedFamilyDependentCheckIns(checkIns, page, size, totalCount));
    }
}