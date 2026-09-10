using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class GetMyUiLanguageQueryHandler :
    IQueryHandler<
        GetMyUiLanguageQuery,
        UiLanguageResponse>
{
    private readonly IIdentityDbContext _dbContext;

    public GetMyUiLanguageQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<UiLanguageResponse>> Handle(
        GetMyUiLanguageQuery request,
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
            return Result<UiLanguageResponse>.Failure(
                AccountErrors.UserNotFound);
        }

        return new UiLanguageResponse(
            user.UiLanguage);
    }
}
