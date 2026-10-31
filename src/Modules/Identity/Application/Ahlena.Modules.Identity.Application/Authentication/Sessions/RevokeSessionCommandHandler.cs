using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Ahlena.Modules.Identity.Application.Authentication.Sessions;

public sealed class RevokeSessionCommandHandler :
    ICommandHandler<RevokeSessionCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RevokeSessionCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        RevokeSessionCommand request,
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
            "Revoked by the user.",
            utcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}