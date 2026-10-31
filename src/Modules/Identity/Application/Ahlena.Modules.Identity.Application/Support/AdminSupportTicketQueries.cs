using MediatR;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Application.Users;
using Ahlena.Modules.Identity.Domain.Support;

namespace Ahlena.Modules.Identity.Application.Support;

public sealed record SupportTicketReplyDto(
    Guid ReplyId,
    Guid ActorUserId,
    string Body,
    DateTime CreatedOnUtc);

/// <summary>
/// One inbox row. <c>DisplayName</c> and <c>AccountType</c> describe the
/// sender and come from the existing Identity display read
/// (<see cref="GetUserDisplayInfoByIdsQuery"/>): the display name prefers the
/// English full name and falls back to Arabic, and the account type is the
/// sender's first-registered account. Both are <c>null</c> when the sender row
/// is missing or anonymized; the sender's email and phone number are never
/// returned.
/// </summary>
public sealed record AdminSupportTicketListItem(
    Guid TicketId,
    Guid UserId,
    string? DisplayName,
    int? AccountType,
    string Subject,
    int Status,
    DateTime CreatedOnUtc);

public sealed record AdminSupportTicketsPage(
    IReadOnlyList<AdminSupportTicketListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetAdminSupportTicketsQuery(
    int? Status = null,
    int Page = 1,
    int PageSize = 25) : IQuery<AdminSupportTicketsPage>;

public sealed class GetAdminSupportTicketsQueryHandler(
    IIdentityDbContext dbContext,
    ISender sender)
    : IQueryHandler<GetAdminSupportTicketsQuery, AdminSupportTicketsPage>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<Result<AdminSupportTicketsPage>> Handle(
        GetAdminSupportTicketsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        var query = dbContext.SupportTickets.AsNoTracking();

        if (request.Status is { } status)
        {
            query = query.Where(t => (int)t.Status == status);
        }

        int totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(t => t.CreatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // One batched sender lookup for the whole page (never per row).
        IReadOnlyDictionary<Guid, AdminSupportTicketSender> senders =
            await AdminSupportTicketSenders.ResolveAsync(
                sender,
                rows.Select(t => t.UserId).ToArray(),
                cancellationToken);

        var items = rows.Select(t =>
        {
            AdminSupportTicketSender ticketSender = SenderOf(senders, t.UserId);

            return new AdminSupportTicketListItem(
                t.Id.Value,
                t.UserId.Value,
                ticketSender.DisplayName,
                ticketSender.AccountType,
                t.Subject,
                (int)t.Status,
                t.CreatedOnUtc);
        }).ToList();

        return new AdminSupportTicketsPage(items, page, pageSize, totalCount);
    }

    private static AdminSupportTicketSender SenderOf(
        IReadOnlyDictionary<Guid, AdminSupportTicketSender> senders,
        UserId userId) =>
        senders.TryGetValue(userId.Value, out AdminSupportTicketSender info)
            ? info
            : default;
}

/// <summary>
/// One ticket with its replies. <c>DisplayName</c> and <c>AccountType</c>
/// carry the same sender facts and the same null contract as
/// <see cref="AdminSupportTicketListItem"/>; the sender's email and phone
/// number are never returned.
/// </summary>
public sealed record AdminSupportTicketDetail(
    Guid TicketId,
    Guid UserId,
    string? DisplayName,
    int? AccountType,
    string Subject,
    string Message,
    int Status,
    DateTime CreatedOnUtc,
    DateTime? NotifiedOnUtc,
    Guid? StatusChangedByUserId,
    DateTime? StatusChangedOnUtc,
    IReadOnlyList<SupportTicketReplyDto> Replies);

public sealed record GetAdminSupportTicketByIdQuery(
    SupportTicketId TicketId) : IQuery<AdminSupportTicketDetail>;

public sealed class GetAdminSupportTicketByIdQueryHandler(
    IIdentityDbContext dbContext,
    ISender sender)
    : IQueryHandler<GetAdminSupportTicketByIdQuery, AdminSupportTicketDetail>
{
    public async Task<Result<AdminSupportTicketDetail>> Handle(
        GetAdminSupportTicketByIdQuery request,
        CancellationToken cancellationToken)
    {
        var ticket = await dbContext.SupportTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken);

        if (ticket is null)
        {
            return Result<AdminSupportTicketDetail>.Failure(
                SupportTicketErrors.NotFound);
        }

        var replies = await dbContext.SupportTicketReplies
            .AsNoTracking()
            .Where(r => r.TicketId == request.TicketId)
            .OrderBy(r => r.CreatedOnUtc)
            .ToListAsync(cancellationToken);

        var replyDtos = replies.Select(r => new SupportTicketReplyDto(
            r.Id.Value,
            r.ActorUserId.Value,
            r.Body,
            r.CreatedOnUtc)).ToList();

        IReadOnlyDictionary<Guid, AdminSupportTicketSender> senders =
            await AdminSupportTicketSenders.ResolveAsync(
                sender,
                [ticket.UserId],
                cancellationToken);

        AdminSupportTicketSender ticketSender =
            senders.TryGetValue(ticket.UserId.Value, out AdminSupportTicketSender info)
                ? info
                : default;

        return new AdminSupportTicketDetail(
            ticket.Id.Value,
            ticket.UserId.Value,
            ticketSender.DisplayName,
            ticketSender.AccountType,
            ticket.Subject,
            ticket.Message,
            (int)ticket.Status,
            ticket.CreatedOnUtc,
            ticket.NotifiedOnUtc,
            ticket.StatusChangedByUserId?.Value,
            ticket.StatusChangedOnUtc,
            replyDtos);
    }
}

/// <summary>
/// The sender facts the inbox publishes: a single display name and a single
/// display account type. Both are <c>null</c> when the sender cannot be
/// resolved.
/// </summary>
internal readonly record struct AdminSupportTicketSender(
    string? DisplayName,
    int? AccountType);

/// <summary>
/// Batched sender enrichment for the admin inbox. It reuses the existing
/// Identity display read (<see cref="GetUserDisplayInfoByIdsQuery"/>) instead
/// of duplicating its projection: that read already excludes anonymized users
/// (they resolve to null here), never carries email or phone, and orders each
/// user's account types oldest-first, so the first entry is the
/// first-registered account. A failed lookup degrades to no enrichment rather
/// than failing the read.
/// </summary>
internal static class AdminSupportTicketSenders
{
    internal static async Task<IReadOnlyDictionary<Guid, AdminSupportTicketSender>>
        ResolveAsync(
            ISender sender,
            IReadOnlyCollection<UserId> userIds,
            CancellationToken cancellationToken)
    {
        UserId[] distinct = userIds
            .Where(id => id != UserId.Empty)
            .Distinct()
            .ToArray();

        var senders = new Dictionary<Guid, AdminSupportTicketSender>();

        if (distinct.Length == 0)
        {
            return senders;
        }

        Result<IReadOnlyList<UserDisplayInfo>> result =
            await sender.Send(
                new GetUserDisplayInfoByIdsQuery(distinct),
                cancellationToken);

        if (!result.IsSuccess)
        {
            return senders;
        }

        foreach (UserDisplayInfo info in result.Value)
        {
            senders[info.UserId.Value] = new AdminSupportTicketSender(
                DisplayName(info),
                info.AccountTypes.Count > 0 ? info.AccountTypes[0] : null);
        }

        return senders;
    }

    /// <summary>
    /// English full name preferred, Arabic fallback — the same rule the Chat
    /// participant display uses. A blank pair resolves to <c>null</c> so no
    /// placeholder name is invented.
    /// </summary>
    private static string? DisplayName(UserDisplayInfo info)
    {
        string preferred = string.IsNullOrWhiteSpace(info.EnglishFullName)
            ? info.ArabicFullName
            : info.EnglishFullName;

        return string.IsNullOrWhiteSpace(preferred) ? null : preferred;
    }
}