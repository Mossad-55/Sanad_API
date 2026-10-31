using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.API.Security;

/// <summary>
/// Shared session ownership/revocation check used by JWT validation, the Chat
/// hub/publisher, and the push final-send gate. A session is active when its
/// row exists, belongs to the asserted user, is not revoked, is not expired,
/// and the owning user is Active. Expiry uses the session row (refresh
/// lifetime), not the 15-minute access-token lifetime.
/// </summary>
public interface IIdentitySessionValidator
{
    Task<bool> IsSessionActiveAsync(
        UserId userId,
        DeviceSessionId sessionId,
        CancellationToken cancellationToken);

    Task<bool> HasAnyActiveSessionAsync(
        UserId userId,
        DateTime utcNow,
        CancellationToken cancellationToken);
}

public sealed class IdentitySessionValidator(
    IIdentityDbContext dbContext) : IIdentitySessionValidator
{
    public async Task<bool> IsSessionActiveAsync(
        UserId userId,
        DeviceSessionId sessionId,
        CancellationToken cancellationToken)
    {
        if (userId == UserId.Empty ||
            sessionId == DeviceSessionId.Empty)
        {
            return false;
        }

        DateTime utcNow = DateTime.UtcNow;

        var session = await dbContext.DeviceSessions
            .AsNoTracking()
            .Where(item =>
                item.Id == sessionId &&
                item.UserId == userId)
            .Select(item => new
            {
                item.RevokedOnUtc,
                item.ExpiresOnUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        if (session is null ||
            session.RevokedOnUtc.HasValue ||
            session.ExpiresOnUtc <= utcNow)
        {
            return false;
        }

        int? status = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (int?)user.Status)
            .SingleOrDefaultAsync(cancellationToken);

        return status == (int)UserStatus.Active;
    }

    public async Task<bool> HasAnyActiveSessionAsync(
        UserId userId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        if (userId == UserId.Empty)
        {
            return false;
        }

        int? status = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => (int?)user.Status)
            .SingleOrDefaultAsync(cancellationToken);

        if (status != (int)UserStatus.Active)
        {
            return false;
        }

        return await dbContext.DeviceSessions
            .AsNoTracking()
            .AnyAsync(
                item =>
                    item.UserId == userId &&
                    item.RevokedOnUtc == null &&
                    item.ExpiresOnUtc > utcNow,
                cancellationToken);
    }
}
