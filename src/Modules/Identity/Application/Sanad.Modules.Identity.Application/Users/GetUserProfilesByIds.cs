using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed record UserProfileById(
    UserId UserId,
    string ArabicFullName,
    string EnglishFullName,
    string? Email);

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
                .ToListAsync(cancellationToken);

        IReadOnlyList<UserProfileById> profiles =
            users
                .Select(user => new UserProfileById(
                    user.Id,
                    user.ArabicFullName.Value,
                    user.EnglishFullName.Value,
                    user.Email?.Value))
                .ToList();

        return Result<IReadOnlyList<UserProfileById>>.Success(
            profiles);
    }
}
