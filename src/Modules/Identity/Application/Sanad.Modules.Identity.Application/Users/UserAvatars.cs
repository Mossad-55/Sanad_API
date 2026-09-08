using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public static class AvatarStorage
{
    public const string Folder =
        "avatars";
}

public static class AvatarErrors
{
    public static readonly Error UserNotFound =
        new(
            "Identity.Avatar.UserNotFound",
            "User was not found.");

    public static readonly Error NotFound =
        new(
            "Identity.Avatar.NotFound",
            "Avatar was not found.");

    public static readonly Error UnsupportedAccountType =
        new(
            "Identity.Avatar.UnsupportedAccountType",
            "Avatar upload is only available for Family, Medical Caregiver, or Companion Caregiver.");

    public static readonly Error InvalidOperation =
        new(
            "Identity.Avatar.InvalidOperation",
            "The avatar operation is not allowed.");
}

public sealed record UpsertUserAvatarCommand(
    UserId UserId,
    string ImagePath)
    : ICommand;

public sealed class UpsertUserAvatarCommandValidator :
    AbstractValidator<UpsertUserAvatarCommand>
{
    public UpsertUserAvatarCommandValidator()
    {
        RuleFor(command =>
                command.UserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.ImagePath)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class UpsertUserAvatarCommandHandler :
    ICommandHandler<UpsertUserAvatarCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public UpsertUserAvatarCommandHandler(
        IIdentityDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result> Handle(
        UpsertUserAvatarCommand request,
        CancellationToken cancellationToken)
    {
        User? user =
            await _dbContext.Users
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.UserId,
                    cancellationToken);

        if (user is null)
        {
            return Result.Failure(
                AvatarErrors.UserNotFound);
        }

        if (!IsSupportedAccount(
            user))
        {
            return Result.Failure(
                AvatarErrors.UnsupportedAccountType);
        }

        if (user.Status ==
            UserStatus.Blocked)
        {
            return Result.Failure(
                AvatarErrors.InvalidOperation);
        }

        string? previousPath =
            user.AvatarUrl;

        user.ChangeAvatar(
            request.ImagePath);

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(
                previousPath) &&
            previousPath !=
                request.ImagePath)
        {
            await _fileStorage.DeleteAsync(
                previousPath,
                cancellationToken);
        }

        return Result.Success();
    }

    private static bool IsSupportedAccount(
        User user)
    {
        return user.Accounts.Any(
            account =>
                account.AccountType ==
                    AccountType.Family ||
                account.AccountType ==
                    AccountType.MedicalCaregiver ||
                account.AccountType ==
                    AccountType.CompanionCaregiver);
    }
}

public sealed record UserAvatarFileContent(
    string FileName,
    string ContentType,
    Stream Content);

public sealed record GetUserAvatarQuery(
    UserId UserId)
    : IQuery<UserAvatarFileContent>;

public sealed class GetUserAvatarQueryValidator :
    AbstractValidator<GetUserAvatarQuery>
{
    public GetUserAvatarQueryValidator()
    {
        RuleFor(query =>
                query.UserId)
            .NotEqual(
                UserId.Empty);
    }
}

public sealed class GetUserAvatarQueryHandler :
    IQueryHandler<
        GetUserAvatarQuery,
        UserAvatarFileContent>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public GetUserAvatarQueryHandler(
        IIdentityDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<UserAvatarFileContent>> Handle(
        GetUserAvatarQuery request,
        CancellationToken cancellationToken)
    {
        User? user =
            await _dbContext.Users
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item =>
                        item.Id ==
                        request.UserId,
                    cancellationToken);

        if (user is null)
        {
            return Result<UserAvatarFileContent>.Failure(
                AvatarErrors.UserNotFound);
        }

        if (!user.Accounts.Any(
                account =>
                    account.AccountType ==
                        AccountType.Family ||
                    account.AccountType ==
                        AccountType.MedicalCaregiver ||
                    account.AccountType ==
                        AccountType.CompanionCaregiver))
        {
            return Result<UserAvatarFileContent>.Failure(
                AvatarErrors.UnsupportedAccountType);
        }

        if (string.IsNullOrWhiteSpace(
            user.AvatarUrl))
        {
            return Result<UserAvatarFileContent>.Failure(
                AvatarErrors.NotFound);
        }

        Result<PrivateFileContent> file =
            await _fileStorage.OpenReadAsync(
                user.AvatarUrl,
                cancellationToken);

        if (file.IsFailure)
        {
            return Result<UserAvatarFileContent>.Failure(
                file.Error);
        }

        string extension =
            file.Value.ContentType.ToLowerInvariant() switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".bin"
            };

        string fileName =
            $"avatar-{user.Id.Value:N}{extension}";

        return new UserAvatarFileContent(
            fileName,
            file.Value.ContentType,
            file.Value.Content);
    }
}
