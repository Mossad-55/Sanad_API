using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users.Admin;

/// <summary>
/// Read-only SuperAdmin aggregates over Identity users: distinct totals, current
/// status/account-type counts and the 12 latest Cairo-calendar-month registration
/// series. Aggregate only - never row-level identity data.
/// </summary>
public sealed record AdminUserStatisticsQuery : IQuery<AdminUserStatisticsResponse>;

public sealed record UserRegistrationMonthBucket(string Month, int Count);

public sealed record UserStatusCount(UserStatus Status, int Count);

public sealed record AccountTypeCount(AccountType AccountType, int DistinctUserCount);

public sealed record AdminUserStatisticsResponse(
    int TotalUsers,
    IReadOnlyList<UserStatusCount> UsersByStatus,
    IReadOnlyList<AccountTypeCount> UsersByAccountType,
    IReadOnlyList<UserRegistrationMonthBucket> NewRegistrationsByMonth);

public sealed class AdminUserStatisticsQueryHandler(IIdentityDbContext db, IDateTimeProvider clock)
    : IQueryHandler<AdminUserStatisticsQuery, AdminUserStatisticsResponse>
{
    private const int SeriesLengthMonths = 12;

    public async Task<Result<AdminUserStatisticsResponse>> Handle(
        AdminUserStatisticsQuery request,
        CancellationToken ct)
    {
        TimeZoneInfo cairo = StatisticsCairoZone();
        DateTime nowUtc = clock.UtcNow;

        // 12 latest Cairo calendar months, including the current partial month.
        // Each month start is computed in Cairo local time and converted to a UTC
        // instant so the SQL filter uses real UTC boundaries (no host-local TZ).
        DateTime nowLocal = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), cairo);
        var monthStartsUtc = new List<DateTime>(SeriesLengthMonths);
        for (int i = SeriesLengthMonths - 1; i >= 0; i--)
        {
            int year = nowLocal.Year;
            int month = nowLocal.Month - i;
            while (month <= 0)
            {
                month += 12;
                year--;
            }

            DateTime localMidnight = new DateTime(
                year, month, 1, 0, 0, 0, DateTimeKind.Unspecified);
            monthStartsUtc.Add(TimeZoneInfo.ConvertTimeToUtc(localMidnight, cairo));
        }

        DateTime windowStartUtc = monthStartsUtc[0];
        DateTime windowExclusiveEndUtc = monthStartsUtc[^1].AddMonths(1);
        // Recompute the exclusive end from local Cairo (AddMonths on UTC instants
        // can drift across DST; Cairo has no DST but keep it local-correct).
        DateTime lastLocalMidnight = new DateTime(
            nowLocal.Year, nowLocal.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        DateTime nextLocalMidnight = lastLocalMidnight.AddMonths(1);
        windowExclusiveEndUtc = TimeZoneInfo.ConvertTimeToUtc(nextLocalMidnight, cairo);

        // Project only CreatedOnUtc (no PII rows) for the monthly series.
        List<DateTime> createdOnValues = await db.Users
            .AsNoTracking()
            .Where(user =>
                user.CreatedOnUtc >= windowStartUtc &&
                user.CreatedOnUtc < windowExclusiveEndUtc)
            .Select(user => user.CreatedOnUtc)
            .ToListAsync(ct);

        Dictionary<DateTime, int> countsByMonth = createdOnValues
            .GroupBy(utc => CairoMonthStart(utc, cairo))
            .ToDictionary(group => group.Key, group => group.Count());

        var buckets = new List<UserRegistrationMonthBucket>(SeriesLengthMonths);
        foreach (DateTime startUtc in monthStartsUtc)
        {
            buckets.Add(new UserRegistrationMonthBucket(
                TimeZoneInfo.ConvertTimeFromUtc(startUtc, cairo).ToString("yyyy-MM", CultureInfo.InvariantCulture),
                countsByMonth.TryGetValue(startUtc, out int count) ? count : 0));
        }

        int totalUsers = await db.Users.AsNoTracking().CountAsync(ct);

        var statusGroups = await db.Users.AsNoTracking()
            .GroupBy(user => user.Status)
            .Select(group => new { Status = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        Dictionary<UserStatus, int> statusCounts = statusGroups
            .ToDictionary(row => row.Status, row => row.Count);

        // Account-type counts: one row per (user, account) via SelectMany over the
        // owned user_accounts collection. Translatable join; no PII projected.
        var accountGroups = await db.Users
            .AsNoTracking()
            .SelectMany(user => user.Accounts)
            .GroupBy(account => account.AccountType)
            .Select(group => new { Type = group.Key, Count = group.Count() })
            .ToListAsync(ct);
        Dictionary<AccountType, int> accountCounts = accountGroups
            .ToDictionary(row => row.Type, row => row.Count);

        return new AdminUserStatisticsResponse(
            totalUsers,
            Enum.GetValues<UserStatus>()
                .Select(value => new UserStatusCount(value, statusCounts.TryGetValue(value, out int c) ? c : 0))
                .ToArray(),
            Enum.GetValues<AccountType>()
                .Select(value => new AccountTypeCount(value, accountCounts.TryGetValue(value, out int c) ? c : 0))
                .ToArray(),
            buckets);
    }

    internal static DateTime CairoMonthStart(DateTime utc, TimeZoneInfo cairo)
    {
        DateTime local = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(utc, DateTimeKind.Utc), cairo);
        DateTime localMidnight = new DateTime(
            local.Year, local.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, cairo);
    }

    internal static TimeZoneInfo StatisticsCairoZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Egypt Standard Time");
        }
    }
}
