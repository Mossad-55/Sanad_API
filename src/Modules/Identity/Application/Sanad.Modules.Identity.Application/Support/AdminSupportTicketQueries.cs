using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Support;

public sealed record SupportTicketReplyDto(
    Guid ReplyId,
    Guid ActorUserId,
    string Body,
    DateTime CreatedOnUtc);

public sealed record AdminSupportTicketListItem(
    Guid TicketId,
    Guid UserId,
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

public sealed class GetAdminSupportTicketsQueryHandler(IIdentityDbContext dbContext)
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

        var items = rows.Select(t => new AdminSupportTicketListItem(
            t.Id.Value,
            t.UserId.Value,
            t.Subject,
            (int)t.Status,
            t.CreatedOnUtc)).ToList();

        return new AdminSupportTicketsPage(items, page, pageSize, totalCount);
    }
}

public sealed record AdminSupportTicketDetail(
    Guid TicketId,
    Guid UserId,
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

public sealed class GetAdminSupportTicketByIdQueryHandler(IIdentityDbContext dbContext)
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

        return new AdminSupportTicketDetail(
            ticket.Id.Value,
            ticket.UserId.Value,
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