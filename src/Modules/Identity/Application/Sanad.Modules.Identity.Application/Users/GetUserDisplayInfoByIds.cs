using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record UserDisplayInfo(
    UserId UserId,
    string ArabicFullName,
    string EnglishFullName,
    IReadOnlyList<int> AccountTypes);

public sealed record GetUserDisplayInfoByIdsQuery(
    IReadOnlyCollection<UserId> UserIds)
    : IQuery<IReadOnlyList<UserDisplayInfo>>;

public sealed class GetUserDisplayInfoByIdsQueryHandler
    : IQueryHandler<GetUserDisplayInfoByIdsQuery, IReadOnlyList<UserDisplayInfo>>
{
    private readonly IIdentityDbContext _dbContext;

    public GetUserDisplayInfoByIdsQueryHandler(IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<UserDisplayInfo>>> Handle(
        GetUserDisplayInfoByIdsQuery request,
        CancellationToken cancellationToken)
    {
        UserId[] userIds = request.UserIds.Distinct().ToArray();
        if (userIds.Length == 0)
        {
            return Result<IReadOnlyList<UserDisplayInfo>>.Success(Array.Empty<UserDisplayInfo>());
        }

        List<User> users = await _dbContext.Users
            .AsNoTracking()
            .Where(user => userIds.Contains(user.Id))
            .Include(user => user.Accounts)
            .ToListAsync(cancellationToken);

        // Anonymized identities yield no enrichment downstream (callers map
        // missing rows to null display fields). Detection mirrors the
        // canonical Identity anonymization check: null email, Blocked
        // status, "محذوف"/"Deleted" sentinel names, unverified channels.
        IReadOnlyList<UserDisplayInfo> info = users
            .Where(user => !(user.Email is null
                && user.Status == UserStatus.Blocked
                && user.ArabicFullName.Value == "محذوف"
                && user.EnglishFullName.Value == "Deleted"
                && !user.EmailVerified
                && !user.PhoneVerified))
            .Select(user => new UserDisplayInfo(
                user.Id,
                user.ArabicFullName.Value,
                user.EnglishFullName.Value,
                user.Accounts
                    .OrderBy(account => account.CreatedOnUtc)
                    .ThenBy(account => account.AccountType)
                    .Select(account => (int)account.AccountType)
                    .ToArray()))
            .ToList();

        return Result<IReadOnlyList<UserDisplayInfo>>.Success(info);
    }
}
