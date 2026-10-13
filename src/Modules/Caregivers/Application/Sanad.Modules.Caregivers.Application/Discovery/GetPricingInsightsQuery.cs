using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Domain.Bookings;

namespace Sanad.Modules.Caregivers.Application.Discovery;

/// <summary>
/// Chat Plus pricing insights for one booked area/service pair. Exact
/// approved calculation (plan §3): one clock instant per query, 90-day
/// UTC completion window, eligible sample = distinct Families caregiver
/// bookings Completed + paid + valid in-window completion + EGP snapshot +
/// positive base fee + immutable booked attribution to this pair; booked
/// fee snapshots only (no platform fee/tax, no current prices); hourly
/// pool = Hourly shifts rated by booked hours with the supported overnight
/// rule; eight-hour pool = EightHourShift base fees verbatim; each pool
/// needs ≥5 distinct bookings or its range is null independently. Never
/// fake defaults, zero ranges, or partially-null min/max objects.
/// </summary>
public sealed record GetPricingInsightsQuery(
    Guid AreaId,
    Guid ServiceId)
    : IQuery<PricingInsightsResponse>;

public sealed record PriceRangeResponse(decimal Min, decimal Max);

public sealed record PricingInsightsResponse(
    string Currency,
    PriceRangeResponse? MarketHourlyPrice,
    PriceRangeResponse? MostRequestedEightHourDayPrice,
    DateTime UpdatedOnUtc);

public static class PricingInsightsErrors
{
    public static readonly Error InvalidFilter =
        new(
            "Caregivers.PricingInsights.InvalidFilter",
            "The pricing filter is invalid.");
}

public sealed class GetPricingInsightsQueryHandler
    : IQueryHandler<GetPricingInsightsQuery, PricingInsightsResponse>
{
    /// <summary>Minimum distinct eligible bookings per pool (N=5).</summary>
    public const int MinimumPoolSize = 5;

    /// <summary>Eligibility window in days (90).</summary>
    public const int WindowDays = 90;

    private readonly IFamiliesDbContext _families;
    private readonly ICaregiversDbContext _caregivers;
    private readonly IDateTimeProvider _clock;

    public GetPricingInsightsQueryHandler(
        IFamiliesDbContext families,
        ICaregiversDbContext caregivers,
        IDateTimeProvider clock)
    {
        _families = families;
        _caregivers = caregivers;
        _clock = clock;
    }

    public async Task<Result<PricingInsightsResponse>> Handle(
        GetPricingInsightsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.AreaId == Guid.Empty || request.ServiceId == Guid.Empty)
        {
            return Result<PricingInsightsResponse>.Failure(
                PricingInsightsErrors.InvalidFilter);
        }

        var areaKey = new AreaId(request.AreaId);
        var serviceKey = new ServiceId(request.ServiceId);

        bool areaActive = await _caregivers.Areas
            .AsNoTracking()
            .AnyAsync(
                area => area.Id == areaKey && area.IsActive,
                cancellationToken);

        bool serviceActive = await _caregivers.Services
            .AsNoTracking()
            .AnyAsync(
                service => service.Id == serviceKey && service.IsActive,
                cancellationToken);

        if (!areaActive || !serviceActive)
        {
            return Result<PricingInsightsResponse>.Failure(
                PricingInsightsErrors.InvalidFilter);
        }

        // One injected clock instant per query: the window and the response
        // stamp share it, so concurrent commits cannot mix instants.
        DateTime now = _clock.UtcNow;
        DateTime windowStart = now.AddDays(-WindowDays);

        // One bounded database pass (pair + window + paid/completed in SQL);
        // both pools split from the same rows so the response never mixes
        // different concurrently committed data. Each row is one booking —
        // counts describe bookings, never joined row counts.
        var candidates = await _families.Bookings
            .AsNoTracking()
            .Where(booking =>
                booking.Status == BookingStatus.Completed &&
                booking.PaidOnUtc != null &&
                booking.CompletedOnUtc != null &&
                booking.CompletedOnUtc >= windowStart &&
                booking.CompletedOnUtc <= now &&
                booking.BookingAreaId == request.AreaId &&
                booking.BookingServiceId == request.ServiceId &&
                booking.PriceSnapshot.Currency == "EGP" &&
                booking.PriceSnapshot.BaseCaregiverFee > 0)
            .Select(booking => new PricingCandidate(
                booking.ShiftType,
                booking.StartTime,
                booking.EndTime,
                booking.PriceSnapshot.BaseCaregiverFee))
            .ToListAsync(cancellationToken);

        List<decimal> hourlyRates = [];
        List<decimal> eightHourPrices = [];

        foreach (PricingCandidate candidate in candidates)
        {
            if (candidate.ShiftType == BookingShiftType.Hourly)
            {
                if (TryHourlyRate(candidate, out decimal rate))
                {
                    hourlyRates.Add(rate);
                }
            }
            else if (candidate.ShiftType == BookingShiftType.EightHourShift)
            {
                eightHourPrices.Add(candidate.BaseCaregiverFee);
            }
            // Medical home visits, 12h, and 24h rates never enter either
            // pool: the pools are Hourly-only and EightHourShift-only.
        }

        return Result<PricingInsightsResponse>.Success(
            new PricingInsightsResponse(
                "EGP",
                RangeOrNull(hourlyRates),
                RangeOrNull(eightHourPrices),
                now));
    }

    /// <summary>
    /// Hourly rate from the snapshotted base fee over booked hours. Duration
    /// uses the supported overnight rule (an end at/before the start wraps
    /// past midnight); a zero-length stay is ambiguous legacy data and is
    /// excluded, as is any non-positive duration. Each rate rounds to two
    /// decimals (banker's) before min/max.
    /// </summary>
    internal static bool TryHourlyRate(PricingCandidate candidate, out decimal rate)
    {
        rate = 0;

        if (candidate.StartTime == candidate.EndTime)
        {
            return false;
        }

        TimeSpan start = candidate.StartTime.ToTimeSpan();
        TimeSpan end = candidate.EndTime.ToTimeSpan();

        TimeSpan duration = end <= start
            ? TimeSpan.FromHours(24) - (start - end)
            : end - start;

        if (duration <= TimeSpan.Zero)
        {
            return false;
        }

        decimal hours = (decimal)duration.TotalHours;

        if (hours <= 0)
        {
            return false;
        }

        rate = decimal.Round(candidate.BaseCaregiverFee / hours, 2, MidpointRounding.ToEven);
        return rate > 0;
    }

    private static PriceRangeResponse? RangeOrNull(List<decimal> samples) =>
        samples.Count < MinimumPoolSize
            ? null
            : new PriceRangeResponse(samples.Min(), samples.Max());

    internal sealed record PricingCandidate(
        BookingShiftType ShiftType,
        TimeOnly StartTime,
        TimeOnly EndTime,
        decimal BaseCaregiverFee);
}
