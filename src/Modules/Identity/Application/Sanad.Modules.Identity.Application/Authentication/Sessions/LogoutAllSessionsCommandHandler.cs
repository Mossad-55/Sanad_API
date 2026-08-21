using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed class LogoutAllSessionsCommandHandler :
    ICommandHandler<LogoutAllSessionsCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LogoutAllSessionsCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        LogoutAllSessionsCommand request,
        CancellationToken cancellationToken)
    {
        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        DeviceSession[] nonRevokedSessions =
            await _dbContext.DeviceSessions
                .Where(item =>
                    item.UserId ==
                        request.CurrentUserId &&
                    item.RevokedOnUtc ==
                        null)
                .ToArrayAsync(
                    cancellationToken);

        foreach (
            DeviceSession session
            in nonRevokedSessions)
        {
            session.Revoke(
                "User logged out from all sessions.",
                utcNow);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}