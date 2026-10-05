using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;
using Microsoft.EntityFrameworkCore;

namespace Sanad.Modules.Caregivers.Application.Earnings;

public sealed record CaregiverEarningsSummaryResponse(
    Guid CaregiverId,
    string NameArabic,
    string NameEnglish,
    decimal TotalEarnings,
    int TotalBookings,
    decimal AverageRating,
    int CompletedBookings,
    decimal EarningsThisPeriod);

public sealed record GetCaregiverEarningsSummaryQuery(
    Guid CaregiverId,
    UserId ActorUserId) : IQuery<CaregiverEarningsSummaryResponse>;

public sealed class GetCaregiverEarningsSummaryQueryHandler(
    ICaregiversDbContext dbContext,
    IFamiliesDbContext familiesDb) : IQueryHandler<
    GetCaregiverEarningsSummaryQuery,
    CaregiverEarningsSummaryResponse>
{
    public async Task<Result<CaregiverEarningsSummaryResponse>> Handle(
        GetCaregiverEarningsSummaryQuery request,
        CancellationToken cancellationToken)
    {
        var caregiver = await dbContext.Caregivers
            .AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Id == new CaregiverId(request.CaregiverId) &&
                     c.UserId == request.ActorUserId,
                cancellationToken);

        if (caregiver is null)
        {
            return Result<CaregiverEarningsSummaryResponse>.Failure(
                new Error("Caregivers.NotFound", "Caregiver not found."));
        }

        var userHeader = await dbContext.GetCaregiverUserHeaderAsync(
            caregiver.UserId,
            cancellationToken);

        if (userHeader is null)
        {
            return Result<CaregiverEarningsSummaryResponse>.Failure(
                new Error("Caregivers.NotFound", "Caregiver identity account not found."));
        }

        var caregiverBookings = familiesDb.Bookings.AsNoTracking()
            .Where(b => b.CaregiverId == caregiver.Id);
        var completedBookingsQuery = caregiverBookings
            .Where(b => b.Status == BookingStatus.Completed && b.CompletedOnUtc.HasValue);

        var totalEarnings = await completedBookingsQuery
            .SumAsync(b => b.PriceSnapshot.BaseCaregiverFee, cancellationToken);

        var totalBookings = await caregiverBookings.CountAsync(cancellationToken);
        var completedBookings = await completedBookingsQuery.CountAsync(cancellationToken);
        var utcNow = DateTime.UtcNow;
        var periodStartUtc = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var earningsThisPeriod = await completedBookingsQuery
            .Where(b => b.CompletedOnUtc >= periodStartUtc)
            .SumAsync(b => b.PriceSnapshot.BaseCaregiverFee, cancellationToken);

        return Result<CaregiverEarningsSummaryResponse>.Success(
            new CaregiverEarningsSummaryResponse(
                caregiver.Id.Value,
                userHeader.ArabicFullName,
                userHeader.EnglishFullName,
                totalEarnings,
                totalBookings,
                caregiver.AverageRating,
                completedBookings,
                earningsThisPeriod));
    }
}
