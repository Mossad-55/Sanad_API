using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Notifications.Application.Abstractions.Data;

namespace Sanad.Modules.Notifications.Application.Notifications;

public sealed record AdminNotificationRecord(Guid Id, string Category, string Type,
    DateTime CreatedOnUtc, DateTime? ReadOnUtc);
public sealed record AdminNotificationPage(IReadOnlyList<AdminNotificationRecord> Items, int Page, int PageSize, bool HasMore);
public sealed record ListAdminNotificationsQuery(int Page = 1, int PageSize = 20) : IQuery<AdminNotificationPage>;

public sealed class ListAdminNotificationsQueryValidator : AbstractValidator<ListAdminNotificationsQuery>
{
    public ListAdminNotificationsQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 100_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public sealed class ListAdminNotificationsQueryHandler(INotificationsDbContext db)
    : IQueryHandler<ListAdminNotificationsQuery, AdminNotificationPage>
{
    public async Task<Result<AdminNotificationPage>> Handle(ListAdminNotificationsQuery request, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var rows = await db.Notifications.AsNoTracking()
            .Where(x => x.CreatedOnUtc >= cutoff)
            .OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id)
            .Skip((request.Page - 1) * request.PageSize).Take(request.PageSize + 1)
            .Select(x => new AdminNotificationRecord(x.Id, x.Category, x.Type, x.CreatedOnUtc, x.ReadOnUtc))
            .ToListAsync(ct);

        var hasMore = rows.Count > request.PageSize;
        if (hasMore) rows.RemoveAt(rows.Count - 1);
        return new AdminNotificationPage(rows, request.Page, request.PageSize, hasMore);
    }
}
