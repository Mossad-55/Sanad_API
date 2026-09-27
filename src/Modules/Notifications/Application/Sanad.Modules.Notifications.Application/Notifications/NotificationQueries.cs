using System.Globalization;
using System.Text;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Notifications.Application.Abstractions.Data;

namespace Sanad.Modules.Notifications.Application.Notifications;

public sealed record NotificationResponse(Guid Id, string Category, string Type, string Title, string Body,
    string DestinationEntityKind, Guid DestinationEntityId, DateTime CreatedOnUtc, DateTime? ReadOnUtc);
public sealed record NotificationPage(IReadOnlyList<NotificationResponse> Items, string? NextCursor, int UnreadCount);
public sealed record ListNotificationsQuery(Guid UserId, string? Cursor, int PageSize = 20) : IQuery<NotificationPage>;
public sealed class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsQueryValidator()
    { RuleFor(x => x.UserId).NotEmpty(); RuleFor(x => x.PageSize).InclusiveBetween(1, 100); }
}
public sealed record GetUnreadNotificationCountQuery(Guid UserId) : IQuery<int>;
public sealed record MarkNotificationReadCommand(Guid UserId, Guid NotificationId) : ICommand;
public sealed record MarkAllNotificationsReadCommand(Guid UserId) : ICommand;

internal static class NotificationCursor
{
    public static string Encode(DateTime created, Guid id) => Convert.ToBase64String(Encoding.UTF8.GetBytes($"{created.Ticks}:{id:N}"));
    public static bool TryDecode(string? value, out DateTime created, out Guid id)
    {
        created = default; id = default;
        try { var p = Encoding.UTF8.GetString(Convert.FromBase64String(value!)).Split(':');
            return p.Length == 2 && long.TryParse(p[0], out var ticks) && Guid.TryParse(p[1], out id)
                && (created = new DateTime(ticks, DateTimeKind.Utc)).Kind == DateTimeKind.Utc; }
        catch { return false; }
    }
}

public sealed class ListNotificationsQueryHandler(INotificationsDbContext db)
    : IQueryHandler<ListNotificationsQuery, NotificationPage>
{
    public async Task<Result<NotificationPage>> Handle(ListNotificationsQuery r, CancellationToken ct)
    {
        var cutoff = DateTime.UtcNow.AddYears(-1);
        var query = db.Notifications.AsNoTracking().Where(x => x.RecipientUserId == r.UserId && x.CreatedOnUtc >= cutoff);
        var unread = await query.Where(x => x.ReadOnUtc == null).CountAsync(ct);
        if (r.Cursor is not null)
        {
            if (!NotificationCursor.TryDecode(r.Cursor, out var date, out var id))
                return new Error("Notifications.InvalidCursor", "The notification cursor is invalid.");
            query = query.Where(x => x.CreatedOnUtc < date || (x.CreatedOnUtc == date && x.Id.CompareTo(id) < 0));
        }
        var rows = await query.OrderByDescending(x => x.CreatedOnUtc).ThenByDescending(x => x.Id).Take(r.PageSize + 1).ToListAsync(ct);
        var hasNext = rows.Count > r.PageSize; if (hasNext) rows.RemoveAt(rows.Count - 1);
        var items = rows.Select(x => new NotificationResponse(x.Id, x.Category, x.Type, x.Title, x.Body,
            x.DestinationEntityKind, x.DestinationEntityId, x.CreatedOnUtc, x.ReadOnUtc)).ToList();
        return new NotificationPage(items, hasNext && rows.Count > 0 ? NotificationCursor.Encode(rows[^1].CreatedOnUtc, rows[^1].Id) : null, unread);
    }
}
public sealed class GetUnreadNotificationCountQueryHandler(INotificationsDbContext db) : IQueryHandler<GetUnreadNotificationCountQuery, int>
{ public async Task<Result<int>> Handle(GetUnreadNotificationCountQuery r, CancellationToken ct) => await db.Notifications.CountAsync(x => x.RecipientUserId == r.UserId && x.ReadOnUtc == null && x.CreatedOnUtc >= DateTime.UtcNow.AddYears(-1), ct); }
public sealed class MarkNotificationReadCommandHandler(INotificationsDbContext db) : ICommandHandler<MarkNotificationReadCommand>
{ public async Task<Result> Handle(MarkNotificationReadCommand r, CancellationToken ct) { var n = await db.Notifications.SingleOrDefaultAsync(x => x.Id == r.NotificationId && x.RecipientUserId == r.UserId && x.CreatedOnUtc >= DateTime.UtcNow.AddYears(-1), ct); if (n is null) return Result.Failure(new Error("Notifications.NotFound", "The notification was not found.")); n.MarkRead(DateTime.UtcNow); await db.SaveChangesAsync(ct); return Result.Success(); } }
public sealed class MarkAllNotificationsReadCommandHandler(INotificationsDbContext db) : ICommandHandler<MarkAllNotificationsReadCommand>
{ public async Task<Result> Handle(MarkAllNotificationsReadCommand r, CancellationToken ct) { var items = await db.Notifications.Where(x => x.RecipientUserId == r.UserId && x.ReadOnUtc == null && x.CreatedOnUtc >= DateTime.UtcNow.AddYears(-1)).ToListAsync(ct); var now = DateTime.UtcNow; foreach (var n in items) n.MarkRead(now); await db.SaveChangesAsync(ct); return Result.Success(); } }
