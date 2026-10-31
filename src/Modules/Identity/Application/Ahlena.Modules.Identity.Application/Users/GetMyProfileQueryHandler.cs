using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

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
            // Avatar parity (Owner lock #34): raw storage keys never leave the
            // API. GET /api/v1/auth/avatar is the authorized route for the
            // caller's own avatar, so the profile wires its path, never the key.
            user.AvatarUrl is null ? null : "/api/v1/auth/avatar");
    }
}
