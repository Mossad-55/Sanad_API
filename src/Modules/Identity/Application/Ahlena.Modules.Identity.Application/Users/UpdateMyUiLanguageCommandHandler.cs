using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Identity.Application.Abstractions.Data;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.Modules.Identity.Application.Users;

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
