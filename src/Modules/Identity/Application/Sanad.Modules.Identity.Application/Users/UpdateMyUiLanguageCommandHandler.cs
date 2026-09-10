using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public sealed class UpdateMyUiLanguageCommandHandler :
    ICommandHandler<
        UpdateMyUiLanguageCommand,
        UiLanguageResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpdateMyUiLanguageCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<UiLanguageResponse>> Handle(
        UpdateMyUiLanguageCommand request,
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

        if (user.Status ==
            UserStatus.Blocked)
        {
            return Result<UiLanguageResponse>.Failure(
                AccountErrors.InvalidOperation);
        }

        user.ChangeUiLanguage(
            request.UiLanguage,
            _dateTimeProvider.UtcNow);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return new UiLanguageResponse(
            user.UiLanguage);
    }
}
