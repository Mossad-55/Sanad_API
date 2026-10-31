using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

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
