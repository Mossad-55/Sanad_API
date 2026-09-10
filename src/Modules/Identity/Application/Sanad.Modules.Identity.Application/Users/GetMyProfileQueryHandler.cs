using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class GetMyProfileQueryHandler :
    IQueryHandler<
        GetMyProfileQuery,
        MyProfileResponse>
{
    private readonly IIdentityDbContext _dbContext;

    public GetMyProfileQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<MyProfileResponse>> Handle(
        GetMyProfileQuery request,
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
            return Result<MyProfileResponse>.Failure(
                AccountErrors.UserNotFound);
        }

        AccountType? accountType =
            user.Accounts
                .Select(account =>
                    (AccountType?)account.AccountType)
                .FirstOrDefault();

        return new MyProfileResponse(
            user.ArabicFullName.Value,
            user.EnglishFullName.Value,
            user.Email?.Value,
            user.PhoneNumber.Value,
            accountType,
            user.EmailVerified,
            user.PhoneVerified,
            user.AvatarUrl);
    }
}
