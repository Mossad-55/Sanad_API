using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Support;

public sealed record MySupportTicketListItem(
    Guid TicketId,
    string Subject,
    int Status,
    DateTime CreatedOnUtc);

public sealed record MySupportTicketsPage(
    IReadOnlyList<MySupportTicketListItem> Items,
    int Page,
    int PageSize,
    int TotalCount);

public sealed record GetMySupportTicketsQuery(
    UserId UserId,
    int Page = 1,
    int PageSize = 25) : IQuery<MySupportTicketsPage>;

public sealed class GetMySupportTicketsQueryHandler(IIdentityDbContext dbContext)
    : IQueryHandler<GetMySupportTicketsQuery, MySupportTicketsPage>
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public async Task<Result<MySupportTicketsPage>> Handle(
        GetMySupportTicketsQuery request,
        CancellationToken cancellationToken)
    {
        int page = Math.Max(1, request.Page);
        int pageSize = Math.Clamp(
            request.PageSize <= 0 ? DefaultPageSize : request.PageSize, 1, MaxPageSize);

        var query = dbContext.SupportTickets.AsNoTracking()
            .Where(t => t.UserId == request.UserId);

        int totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(t => t.CreatedOnUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = rows.Select(t => new MySupportTicketListItem(
            t.Id.Value,
            t.Subject,
            (int)t.Status,
            t.CreatedOnUtc)).ToList();

        return new MySupportTicketsPage(items, page, pageSize, totalCount);
    }
}

public sealed record MySupportTicketDetail(
    Guid TicketId,
    string Subject,
    string Message,
    int Status,
    DateTime CreatedOnUtc,
    DateTime? NotifiedOnUtc,
    IReadOnlyList<SupportTicketReplyDto> Replies);

public sealed record GetMySupportTicketByIdQuery(
    UserId UserId,
    SupportTicketId TicketId) : IQuery<MySupportTicketDetail>;

public sealed class GetMySupportTicketByIdQueryHandler(IIdentityDbContext dbContext)
    : IQueryHandler<GetMySupportTicketByIdQuery, MySupportTicketDetail>
{
    public async Task<Result<MySupportTicketDetail>> Handle(
        GetMySupportTicketByIdQuery request,
        CancellationToken cancellationToken)
    {
        var ticket = await dbContext.SupportTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken);

        if (ticket is null || ticket.UserId != request.UserId)
        {
            return Result<MySupportTicketDetail>.Failure(SupportTicketErrors.NotFound);
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

        return new MySupportTicketDetail(
            ticket.Id.Value,
            ticket.Subject,
            ticket.Message,
            (int)ticket.Status,
            ticket.CreatedOnUtc,
            ticket.NotifiedOnUtc,
            replyDtos);
    }
}