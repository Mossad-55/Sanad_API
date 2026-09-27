using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Notifications.Application.Abstractions.Data;

namespace Sanad.Modules.Notifications.Application.Notifications;

public sealed record AdminNotificationRecord(Guid Id, string Category, string Type,
    DateTime CreatedOnUtc, DateTime? ReadOnUtc);
public sealed record AdminNotificationPage(IReadOnlyList<AdminNotificationRecord> Items, int Page, int PageSize, bool HasMore);
public sealed record AdminNotificationCategoryCount(string Category, int Count);
public sealed record AdminNotificationAggregate(int TotalCount, int ReadCount, int UnreadCount,
    IReadOnlyList<AdminNotificationCategoryCount> CategoryCounts);

public sealed record ListAdminNotificationsQuery(
    int Page = 1, int PageSize = 20, string? Category = null, string? Type = null,
    DateOnly? StartDate = null, DateOnly? EndDate = null) : IQuery<AdminNotificationPage>;
public sealed record GetAdminNotificationQuery(Guid NotificationId) : IQuery<AdminNotificationRecord>;
public sealed record GetAdminNotificationTimelineQuery(DateOnly StartDate, DateOnly EndDate, int Page = 1, int PageSize = 20)
    : IQuery<AdminNotificationPage>;
public sealed record GetAdminNotificationAggregateQuery(DateOnly? StartDate = null, DateOnly? EndDate = null)
    : IQuery<AdminNotificationAggregate>;

internal static class AdminNotificationQueryRules
{
    public static readonly Error InvalidPage = new("Notifications.AdminNotification.InvalidPage", "Page and pageSize must each be between 1 and 100.");
    public static readonly Error InvalidRange = new("Notifications.AdminNotification.InvalidDateRange", "The notification date range must be an inclusive range of 31 days or fewer.");

    public static bool IsValidRange(DateOnly start, DateOnly end) =>
        end >= start && end.DayNumber - start.DayNumber < 31;

    public static bool IsValidOptionalRange(DateOnly? start, DateOnly? end) =>
        !start.HasValue || !end.HasValue || IsValidRange(start.Value, end.Value);

    public static DateTime UtcStart(DateOnly date) => date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}

public sealed class ListAdminNotificationsQueryValidator : AbstractValidator<ListAdminNotificationsQuery>
{
    public ListAdminNotificationsQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 100);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x).Must(x => !x.StartDate.HasValue || !x.EndDate.HasValue ||
                AdminNotificationQueryRules.IsValidRange(x.StartDate.Value, x.EndDate.Value))
            .WithName("DateRange").WithMessage("The notification date range must be an inclusive range of 31 days or fewer.");
    }
}

public sealed class GetAdminNotificationTimelineQueryValidator : AbstractValidator<GetAdminNotificationTimelineQuery>
{
    public GetAdminNotificationTimelineQueryValidator()
    {
        RuleFor(x => x).Must(x => AdminNotificationQueryRules.IsValidRange(x.StartDate, x.EndDate))
            .WithName("DateRange").WithMessage("The notification timeline must use an inclusive range of 31 days or fewer.");
        RuleFor(x => x.Page).InclusiveBetween(1, 100);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class GetAdminNotificationAggregateQueryValidator : AbstractValidator<GetAdminNotificationAggregateQuery>
{
    public GetAdminNotificationAggregateQueryValidator() =>
        RuleFor(x => x).Must(x => !x.StartDate.HasValue || !x.EndDate.HasValue ||
                AdminNotificationQueryRules.IsValidRange(x.StartDate.Value, x.EndDate.Value))
            .WithName("DateRange").WithMessage("The notification date range must be an inclusive range of 31 days or fewer.");
}

public sealed class ListAdminNotificationsQueryHandler(INotificationsDbContext db)
    : IQueryHandler<ListAdminNotificationsQuery, AdminNotificationPage>
{
    public async Task<Result<AdminNotificationPage>> Handle(ListAdminNotificationsQuery request, CancellationToken ct)
    {
        if (request.Page is < 1 or > 100 || request.PageSize is < 1 or > 100)
            return Result<AdminNotificationPage>.Failure(AdminNotificationQueryRules.InvalidPage);
        if (!AdminNotificationQueryRules.IsValidOptionalRange(request.StartDate, request.EndDate))
            return Result<AdminNotificationPage>.Failure(AdminNotificationQueryRules.InvalidRange);

        var cutoff = DateTime.UtcNow.AddYears(-1);
        IQueryable<Sanad.Modules.Notifications.Domain.Notifications.Notification> query =
            db.Notifications.AsNoTracking().Where(x => x.CreatedOnUtc >= cutoff);
        if (!string.IsNullOrWhiteSpace(request.Category))
            query = query.Where(x => x.Category == request.Category.Trim());
        if (!string.IsNullOrWhiteSpace(request.Type))
            query = query.Where(x => x.Type == request.Type.Trim());
        if (request.StartDate.HasValue)
            query = query.Where(x => x.CreatedOnUtc >= AdminNotificationQueryRules.UtcStart(request.StartDate.Value));
        if (request.EndDate.HasValue)
            query = ApplyEndDate(query, request.EndDate.Value);

        var rows = await query.OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize + 1)
            .Select(x => new AdminNotificationRecord(x.Id, x.Category, x.Type, x.CreatedOnUtc, x.ReadOnUtc))
            .ToListAsync(ct);

        var hasMore = rows.Count > request.PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new AdminNotificationPage(rows, request.Page, request.PageSize, hasMore);
    }

    internal static IQueryable<Sanad.Modules.Notifications.Domain.Notifications.Notification> ApplyEndDate(
        IQueryable<Sanad.Modules.Notifications.Domain.Notifications.Notification> query, DateOnly endDate)
    {
        if (endDate == DateOnly.MaxValue) return query;
        var exclusiveEnd = AdminNotificationQueryRules.UtcStart(endDate.AddDays(1));
        return query.Where(x => x.CreatedOnUtc < exclusiveEnd);
    }
}

public sealed class GetAdminNotificationQueryHandler(INotificationsDbContext db)
    : IQueryHandler<GetAdminNotificationQuery, AdminNotificationRecord>
{
    private static readonly Error NotFound = new("Notifications.AdminNotification.NotFound", "The notification was not found.");

    public async Task<Result<AdminNotificationRecord>> Handle(GetAdminNotificationQuery request, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var record = await db.Notifications.AsNoTracking()
            .Where(x => x.Id == request.NotificationId && x.CreatedOnUtc >= cutoff)
            .Select(x => new AdminNotificationRecord(x.Id, x.Category, x.Type, x.CreatedOnUtc, x.ReadOnUtc))
            .SingleOrDefaultAsync(ct);
        return record is null ? Result<AdminNotificationRecord>.Failure(NotFound) : record;
    }
}

public sealed class GetAdminNotificationTimelineQueryHandler(INotificationsDbContext db)
    : IQueryHandler<GetAdminNotificationTimelineQuery, AdminNotificationPage>
{
    public async Task<Result<AdminNotificationPage>> Handle(GetAdminNotificationTimelineQuery request, CancellationToken ct)
    {
        if (request.Page is < 1 or > 100 || request.PageSize is < 1 or > 100)
            return Result<AdminNotificationPage>.Failure(AdminNotificationQueryRules.InvalidPage);
        if (!AdminNotificationQueryRules.IsValidRange(request.StartDate, request.EndDate))
            return Result<AdminNotificationPage>.Failure(AdminNotificationQueryRules.InvalidRange);

        var cutoff = DateTime.UtcNow.AddYears(-1);
        IQueryable<Sanad.Modules.Notifications.Domain.Notifications.Notification> query = db.Notifications.AsNoTracking()
            .Where(x => x.CreatedOnUtc >= cutoff && x.CreatedOnUtc >= AdminNotificationQueryRules.UtcStart(request.StartDate));
        query = ListAdminNotificationsQueryHandler.ApplyEndDate(query, request.EndDate);
        var records = await query.OrderBy(x => x.CreatedOnUtc).ThenBy(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize + 1)
            .Select(x => new AdminNotificationRecord(x.Id, x.Category, x.Type, x.CreatedOnUtc, x.ReadOnUtc))
            .ToListAsync(ct);
        var hasMore = records.Count > request.PageSize;
        if (hasMore) records.RemoveAt(records.Count - 1);
        return new AdminNotificationPage(records, request.Page, request.PageSize, hasMore);
    }
}

public sealed class GetAdminNotificationAggregateQueryHandler(INotificationsDbContext db)
    : IQueryHandler<GetAdminNotificationAggregateQuery, AdminNotificationAggregate>
{
    public async Task<Result<AdminNotificationAggregate>> Handle(GetAdminNotificationAggregateQuery request, CancellationToken ct)
    {
        if (!AdminNotificationQueryRules.IsValidOptionalRange(request.StartDate, request.EndDate))
            return Result<AdminNotificationAggregate>.Failure(AdminNotificationQueryRules.InvalidRange);

        var cutoff = DateTime.UtcNow.AddYears(-1);
        IQueryable<Sanad.Modules.Notifications.Domain.Notifications.Notification> query =
            db.Notifications.AsNoTracking().Where(x => x.CreatedOnUtc >= cutoff);
        if (request.StartDate.HasValue)
            query = query.Where(x => x.CreatedOnUtc >= AdminNotificationQueryRules.UtcStart(request.StartDate.Value));
        if (request.EndDate.HasValue)
            query = ListAdminNotificationsQueryHandler.ApplyEndDate(query, request.EndDate.Value);

        var total = await query.CountAsync(ct);
        var read = await query.CountAsync(x => x.ReadOnUtc != null, ct);
        var categoryRows = await query.GroupBy(x => x.Category)
            .Select(group => new { Category = group.Key, Count = group.Count() })
            .OrderBy(x => x.Category).ToListAsync(ct);
        var categories = categoryRows.Select(x => new AdminNotificationCategoryCount(x.Category, x.Count)).ToList();
        return new AdminNotificationAggregate(total, read, total - read, categories);
    }
}
