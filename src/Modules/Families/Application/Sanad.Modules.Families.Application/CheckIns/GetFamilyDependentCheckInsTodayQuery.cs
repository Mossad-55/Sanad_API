using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Elderlies;

namespace Sanad.Modules.Families.Application.CheckIns;

public sealed record GetFamilyDependentCheckInsTodayQuery(
    UserId UserId,
    ElderlyId DependentId) : IQuery<IReadOnlyList<ElderlyCheckInResponse>>;

public sealed class GetFamilyDependentCheckInsTodayQueryHandler(
    IFamiliesDbContext dbContext,
    IFamilyIdentityGateway identityGateway) : IQueryHandler<
    GetFamilyDependentCheckInsTodayQuery,
    IReadOnlyList<ElderlyCheckInResponse>>
{
    private static readonly Error InvalidTimeZone = new(
        "Families.ElderlyCheckIn.InvalidTimeZone",
        "The elderly profile has an invalid time zone.");

    public async Task<Result<IReadOnlyList<ElderlyCheckInResponse>>> Handle(
        GetFamilyDependentCheckInsTodayQuery request,
        CancellationToken cancellationToken)
    {
        // Resolve the family for the user
        var family = await FamilyAccess.ResolveFamilyAsync(
            dbContext,
            request.UserId,
            cancellationToken);

        if (family is null)
        {
            return Result<IReadOnlyList<ElderlyCheckInResponse>>.Failure(
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
            return Result<IReadOnlyList<ElderlyCheckInResponse>>.Failure(
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
            return Result<IReadOnlyList<ElderlyCheckInResponse>>.Failure(
                InvalidTimeZone);
        }

        // Get today's date in the elderly's timezone
        var nowUtc = DateTime.UtcNow;
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, zone);
        var today = DateOnly.FromDateTime(localNow);

        // Get check-ins for today
        var checkIns = await dbContext.ElderlyCheckIns
            .AsNoTracking()
            .Where(c => c.ElderlyId == elderly.Id && c.LocalDate == today)
            .OrderByDescending(c => c.AnsweredOnUtc)
            .Select(c => new ElderlyCheckInResponse(
                c.Id,
                c.ElderlyId.Value,
                c.Answer,
                c.LocalDate,
                c.AnsweredAtLocalTime,
                c.AnsweredOnUtc,
                elderly.TimeZoneId))
            .ToListAsync(cancellationToken);

        return Result<IReadOnlyList<ElderlyCheckInResponse>>.Success(checkIns);
    }
}