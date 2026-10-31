using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

public sealed record UserProfileById(
    UserId UserId,
    string ArabicFullName,
    string EnglishFullName,
    string? Email,
    string? PhoneNumber,
    string? AvatarUrl,
    int Status,
    DateTime? LastLoginOnUtc,
    int? AccountType);

public sealed record GetUserProfilesByIdsQuery(
    IReadOnlyCollection<UserId> UserIds)
    : IQuery<IReadOnlyList<UserProfileById>>;

public sealed class GetUserProfilesByIdsQueryHandler
    : IQueryHandler<
        GetUserProfilesByIdsQuery,
        IReadOnlyList<UserProfileById>>
{
    private readonly IIdentityDbContext _dbContext;

    public GetUserProfilesByIdsQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<UserProfileById>>> Handle(
        GetUserProfilesByIdsQuery request,
        CancellationToken cancellationToken)
    {
        UserId[] userIds =
            request.UserIds.Distinct().ToArray();

        if (userIds.Length == 0)
        {
            return Result<IReadOnlyList<UserProfileById>>.Success(
                Array.Empty<UserProfileById>());
        }

        List<User> users =
            await _dbContext.Users
                .AsNoTracking()
                .Where(user => userIds.Contains(user.Id))
                .Include(user => user.Accounts)
                .ToListAsync(cancellationToken);

        IReadOnlyList<UserProfileById> profiles =
            users
                .Select(user => new UserProfileById(
                    user.Id,
                    user.ArabicFullName.Value,
                    user.EnglishFullName.Value,
                    user.Email?.Value,
                    user.PhoneNumber.Value,
                    user.AvatarUrl,
                    (int)user.Status,
                    user.LastLoginOnUtc,
                    // A user may hold several account types; consumers that
                    // need one integer (chat participants) get the
                    // first-registered account as the deterministic primary.
                    PrimaryAccountType(user)))
                .ToList();

        return Result<IReadOnlyList<UserProfileById>>.Success(
            profiles);
    }

    private static int? PrimaryAccountType(User user) =>
        user.Accounts
            .OrderBy(account => account.CreatedOnUtc)
            .ThenBy(account => account.AccountType)
            .Select(account => (int?)account.AccountType)
            .FirstOrDefault();
}
