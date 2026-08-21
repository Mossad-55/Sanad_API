using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed class GetActiveSessionsQueryHandler :
    IQueryHandler<
        GetActiveSessionsQuery,
        ActiveSessionsResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public GetActiveSessionsQueryHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<ActiveSessionsResponse>> Handle(
        GetActiveSessionsQuery request,
        CancellationToken cancellationToken)
    {
        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        List<ActiveSessionItem> activeSessions =
            await _dbContext.DeviceSessions
                .Where(item =>
                    item.UserId ==
                        request.CurrentUserId &&
                    item.RevokedOnUtc ==
                        null &&
                    item.ExpiresOnUtc >
                        utcNow)
                .OrderBy(item =>
                    item.CreatedOnUtc)
                .Select(item =>
                    new ActiveSessionItem(
                        item.Id,
                        item.DeviceName,
                        item.Platform,
                        item.AppVersion,
                        item.CreatedOnUtc,
                        item.ExpiresOnUtc,
                        item.LastRotatedOnUtc))
                .ToListAsync(
                    cancellationToken);

        return new ActiveSessionsResponse(
            activeSessions);
    }
}