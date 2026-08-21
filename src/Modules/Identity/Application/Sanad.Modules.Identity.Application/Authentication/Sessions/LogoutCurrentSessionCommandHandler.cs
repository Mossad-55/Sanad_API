using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed class LogoutCurrentSessionCommandHandler :
    ICommandHandler<LogoutCurrentSessionCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public LogoutCurrentSessionCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        LogoutCurrentSessionCommand request,
        CancellationToken cancellationToken)
    {
        DeviceSession? session =
            await _dbContext.DeviceSessions
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.DeviceSessionId,
                    cancellationToken);

        if (session is null)
        {
            return Result.Failure(
                SessionManagementErrors
                    .SessionNotFound);
        }

        if (session.UserId !=
            request.CurrentUserId)
        {
            return Result.Failure(
                SessionManagementErrors
                    .SessionNotOwned);
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        session.Revoke(
            "User logged out.",
            utcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}