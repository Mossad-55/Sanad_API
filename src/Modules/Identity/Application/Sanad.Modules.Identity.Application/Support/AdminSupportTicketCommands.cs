using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Support;

public static class SupportTicketTransitions
{
    public static bool IsAllowed(
        SupportTicketStatus from,
        SupportTicketStatus to) =>
        (from, to) switch
        {
            (SupportTicketStatus.New, SupportTicketStatus.InProgress) => true,
            (SupportTicketStatus.New, SupportTicketStatus.Resolved) => true,
            (SupportTicketStatus.New, SupportTicketStatus.Closed) => true,
            (SupportTicketStatus.InProgress, SupportTicketStatus.Resolved) => true,
            (SupportTicketStatus.InProgress, SupportTicketStatus.Closed) => true,
            (SupportTicketStatus.Resolved, SupportTicketStatus.Closed) => true,
            _ => false
        };
}

public sealed record ChangeSupportTicketStatusCommand(
    SupportTicketId TicketId,
    SupportTicketStatus NewStatus,
    UserId AdminUserId) : ICommand<bool>;

public sealed class ChangeSupportTicketStatusCommandHandler(
    IIdentityDbContext dbContext,
    IDateTimeProvider dateTimeProvider)
    : ICommandHandler<ChangeSupportTicketStatusCommand, bool>
{
    public async Task<Result<bool>> Handle(
        ChangeSupportTicketStatusCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = await dbContext.SupportTickets
            .SingleOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken);

        if (ticket is null)
        {
            return Result<bool>.Failure(SupportTicketErrors.NotFound);
        }

        if (!SupportTicketTransitions.IsAllowed(ticket.Status, request.NewStatus))
        {
            return Result<bool>.Failure(SupportTicketErrors.Conflict);
        }

        ticket.ChangeStatus(request.NewStatus, request.AdminUserId, dateTimeProvider.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}

public sealed record ReplyToSupportTicketCommand(
    SupportTicketId TicketId,
    UserId AdminUserId,
    string Body) : ICommand<SupportTicketReplyDto>;

public sealed class ReplyToSupportTicketCommandHandler(
    IIdentityDbContext dbContext,
    IDateTimeProvider dateTimeProvider,
    IEmailSender emailSender,
    ISupportReplyNotifier notifier)
    : ICommandHandler<ReplyToSupportTicketCommand, SupportTicketReplyDto>
{
    public async Task<Result<SupportTicketReplyDto>> Handle(
        ReplyToSupportTicketCommand request,
        CancellationToken cancellationToken)
    {
        var ticket = await dbContext.SupportTickets
            .SingleOrDefaultAsync(t => t.Id == request.TicketId, cancellationToken);

        if (ticket is null)
        {
            return Result<SupportTicketReplyDto>.Failure(SupportTicketErrors.NotFound);
        }

        if (ticket.Status == SupportTicketStatus.Closed)
        {
            return Result<SupportTicketReplyDto>.Failure(SupportTicketErrors.Conflict);
        }

        var reply = SupportTicketReply.Create(
            ticket.Id,
            request.AdminUserId,
            request.Body,
            dateTimeProvider.UtcNow);

        dbContext.SupportTicketReplies.Add(reply);
        await dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            var user = await dbContext.Users
                .SingleOrDefaultAsync(u => u.Id == ticket.UserId, cancellationToken);
            if (user is not null)
            {
                await emailSender.SendSupportReplyAsync(
                    user.Email?.Value,
                    ticket.Subject,
                    reply.Body,
                    cancellationToken);
            }
        }
        catch
        {
        }

        try
        {
            await notifier.TryNotifyAsync(
                ticket.UserId.Value,
                ticket.Id.Value,
                reply.Body,
                cancellationToken);
        }
        catch
        {
        }

        return new SupportTicketReplyDto(
            reply.Id.Value,
            reply.ActorUserId.Value,
            reply.Body,
            reply.CreatedOnUtc);
    }
}