using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Users;

public static class IdentityDocumentStorage
{
    public const string Folder =
        "identity-documents";
}

public static class IdentityDocumentErrors
{
    public static readonly Error UserNotFound =
        new(
            "Identity.IdentityDocument.UserNotFound",
            "User was not found.");

    public static readonly Error UnsupportedAccountType =
        new(
            "Identity.IdentityDocument.UnsupportedAccountType",
            "National ID upload is only available for Family, Medical Caregiver, or Companion Caregiver.");

    public static readonly Error InvalidOperation =
        new(
            "Identity.IdentityDocument.InvalidOperation",
            "The identity document operation is not allowed.");
}

public sealed record IdentityDocumentResponse(
    bool Uploaded,
    IdentityDocumentVerificationStatus? VerificationStatus,
    string? ReviewReason);

public sealed record UpsertIdentityDocumentCommand(
    UserId UserId,
    string FrontImagePath,
    string BackImagePath)
    : ICommand<IdentityDocumentResponse>;

public sealed class UpsertIdentityDocumentCommandValidator :
    AbstractValidator<UpsertIdentityDocumentCommand>
{
    public UpsertIdentityDocumentCommandValidator()
    {
        RuleFor(command =>
                command.UserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.FrontImagePath)
            .NotEmpty()
            .MaximumLength(
                UserIdentityDocument.MaximumImagePathLength);

        RuleFor(command =>
                command.BackImagePath)
            .NotEmpty()
            .MaximumLength(
                UserIdentityDocument.MaximumImagePathLength);
    }
}

public sealed class UpsertIdentityDocumentCommandHandler :
    ICommandHandler<
        UpsertIdentityDocumentCommand,
        IdentityDocumentResponse>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IFileStorage _fileStorage;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UpsertIdentityDocumentCommandHandler(
        IIdentityDbContext dbContext,
        IFileStorage fileStorage,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result<IdentityDocumentResponse>> Handle(
        UpsertIdentityDocumentCommand request,
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
            return Result<IdentityDocumentResponse>.Failure(
                IdentityDocumentErrors.UserNotFound);
        }

        if (!IdentityDocumentMappings.IsSupportedAccount(
            user))
        {
            return Result<IdentityDocumentResponse>.Failure(
                IdentityDocumentErrors.UnsupportedAccountType);
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        string? previousFrontPath =
            user.IdentityDocument?.FrontImagePath;

        string? previousBackPath =
            user.IdentityDocument?.BackImagePath;

        bool documentAlreadyExists =
            user.IdentityDocument is not null;

        UserStatus previousStatus =
            user.Status;

        try
        {
            if (documentAlreadyExists)
            {
                user.UpdateIdentityDocument(
                    request.FrontImagePath,
                    request.BackImagePath,
                    utcNow);
            }
            else
            {
                user.UploadIdentityDocument(
                    request.FrontImagePath,
                    request.BackImagePath,
                    utcNow);
            }
        }
        catch (DomainException)
        {
            return Result<IdentityDocumentResponse>.Failure(
                IdentityDocumentErrors.InvalidOperation);
        }

        if (documentAlreadyExists &&
            previousStatus ==
                UserStatus.Active &&
            user.Status ==
                UserStatus.PendingVerification)
        {
            DeviceSession[] nonRevokedSessions =
                await _dbContext.DeviceSessions
                    .Where(item =>
                        item.UserId ==
                            user.Id &&
                        item.RevokedOnUtc ==
                            null)
                    .ToArrayAsync(
                        cancellationToken);

            foreach (
                DeviceSession session
                in nonRevokedSessions)
            {
                session.Revoke(
                    "National ID was replaced.",
                    utcNow);
            }
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        await DeleteReplacedFileAsync(
            previousFrontPath,
            request.FrontImagePath,
            cancellationToken);

        await DeleteReplacedFileAsync(
            previousBackPath,
            request.BackImagePath,
            cancellationToken);

        return IdentityDocumentMappings.ToResponse(
            user);
    }

    private async Task DeleteReplacedFileAsync(
        string? previousPath,
        string newPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(
                previousPath) ||
            previousPath ==
                newPath)
        {
            return;
        }

        await _fileStorage.DeleteAsync(
            previousPath,
            cancellationToken);
    }
}

public sealed record GetIdentityDocumentQuery(
    UserId UserId)
    : IQuery<IdentityDocumentResponse>;

public sealed class GetIdentityDocumentQueryValidator :
    AbstractValidator<GetIdentityDocumentQuery>
{
    public GetIdentityDocumentQueryValidator()
    {
        RuleFor(query =>
                query.UserId)
            .NotEqual(
                UserId.Empty);
    }
}

public sealed class GetIdentityDocumentQueryHandler :
    IQueryHandler<
        GetIdentityDocumentQuery,
        IdentityDocumentResponse>
{
    private readonly IIdentityDbContext _dbContext;

    public GetIdentityDocumentQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IdentityDocumentResponse>> Handle(
        GetIdentityDocumentQuery request,
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
            return Result<IdentityDocumentResponse>.Failure(
                IdentityDocumentErrors.UserNotFound);
        }

        if (!IdentityDocumentMappings.IsSupportedAccount(
            user))
        {
            return Result<IdentityDocumentResponse>.Failure(
                IdentityDocumentErrors.UnsupportedAccountType);
        }

        return IdentityDocumentMappings.ToResponse(
            user);
    }
}

internal static class IdentityDocumentMappings
{
    internal static bool IsSupportedAccount(
        User user)
    {
        return user.Accounts.Any(
            account =>
                account.AccountType is
                    AccountType.Family or
                    AccountType.MedicalCaregiver or
                    AccountType.CompanionCaregiver);
    }

    internal static IdentityDocumentResponse ToResponse(
        User user)
    {
        if (user.IdentityDocument is null)
        {
            return new IdentityDocumentResponse(
                false,
                null,
                null);
        }

        return new IdentityDocumentResponse(
            true,
            user.IdentityDocument.VerificationStatus,
            user.IdentityDocument.ReviewReason);
    }
}