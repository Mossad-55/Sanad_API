using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Users;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Support;

public sealed class SubmitSupportRequestCommandHandler :
    ICommandHandler<
        SubmitSupportRequestCommand,
        SupportRequestResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IEmailSender _emailSender;
    private readonly IDateTimeProvider _dateTimeProvider;

    public SubmitSupportRequestCommandHandler(
        IIdentityDbContext dbContext,
        IEmailSender emailSender,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _emailSender = emailSender;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<SupportRequestResponse>> Handle(
        SubmitSupportRequestCommand request,
        CancellationToken cancellationToken)
    {
        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.CurrentUserId,
                    cancellationToken);

        if (user is null)
        {
            return Result<SupportRequestResponse>.Failure(
                AccountErrors.UserNotFound);
        }

        if (user.Status ==
            UserStatus.Blocked)
        {
            return Result<SupportRequestResponse>.Failure(
                AccountErrors.InvalidOperation);
        }

        var ticket = SupportTicket.Create(
            user.Id,
            request.Subject,
            request.Message,
            _dateTimeProvider.UtcNow);

        _dbContext.SupportTickets.Add(ticket);
        await _dbContext.SaveChangesAsync(cancellationToken);

        try
        {
            await _emailSender.SendSupportRequestAsync(
                user.ArabicFullName.Value,
                user.Email?.Value,
                user.PhoneNumber.Value,
                string.Join(", ", user.Accounts.Select(a => a.AccountType.ToString())),
                ticket.Subject,
                ticket.Message,
                cancellationToken);
            ticket.MarkNotified(_dateTimeProvider.UtcNow);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Support request must never fail because mail is down.
            // The ticket stays un-notified (NotifiedOnUtc == null).
        }

        return new SupportRequestResponse(
            ticket.Id,
            ticket.Status,
            ticket.CreatedOnUtc);
    }
}
