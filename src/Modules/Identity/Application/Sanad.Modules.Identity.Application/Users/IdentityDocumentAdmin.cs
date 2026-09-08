using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.UserIdentityDocument;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users;

public enum IdentityDocumentFileSide
{
    Front = 1,
    Back = 2
}

public sealed record AdminIdentityDocumentListItem(
    Guid UserId,
    Guid DocumentId,
    string ArabicFullName,
    string EnglishFullName,
    string PhoneNumber,
    string? Email,
    IReadOnlyList<AccountType> AccountTypes,
    UserStatus UserStatus,
    IdentityDocumentVerificationStatus VerificationStatus,
    string? ReviewReason,
    DateTime UpdatedOnUtc);

public sealed record PagedIdentityDocumentList(
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<AdminIdentityDocumentListItem> Items);

public sealed record GetAdminIdentityDocumentsQuery(
    int Page,
    int PageSize,
    IdentityDocumentVerificationStatus? Status)
    : IQuery<PagedIdentityDocumentList>;

public sealed class GetAdminIdentityDocumentsQueryValidator :
    AbstractValidator<GetAdminIdentityDocumentsQuery>
{
    public GetAdminIdentityDocumentsQueryValidator()
    {
        RuleFor(query =>
                query.Page)
            .GreaterThan(0);

        RuleFor(query =>
                query.PageSize)
            .InclusiveBetween(
                1,
                100);
    }
}

public sealed class GetAdminIdentityDocumentsQueryHandler :
    IQueryHandler<
        GetAdminIdentityDocumentsQuery,
        PagedIdentityDocumentList>
{
    private readonly IIdentityDbContext _dbContext;

    public GetAdminIdentityDocumentsQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedIdentityDocumentList>> Handle(
        GetAdminIdentityDocumentsQuery request,
        CancellationToken cancellationToken)
    {
        IQueryable<User> users =
            _dbContext.Users
                .AsNoTracking()
                .Where(user =>
                    user.IdentityDocument !=
                    null &&
                    user.Accounts.Any(
                        account =>
                            account.AccountType ==
                                AccountType.Family ||
                            account.AccountType ==
                                AccountType.MedicalCaregiver ||
                            account.AccountType ==
                                AccountType.CompanionCaregiver));

        if (request.Status.HasValue)
        {
            IdentityDocumentVerificationStatus status =
                request.Status.Value;

            users = users.Where(
                user =>
                    user.IdentityDocument!
                        .VerificationStatus ==
                    status);
        }

        int totalCount =
            await users.CountAsync(
                cancellationToken);

        List<User> page =
            await users
                .OrderByDescending(user =>
                    user.IdentityDocument!
                        .UpdatedOnUtc)
                .Skip(
                    (request.Page - 1) *
                    request.PageSize)
                .Take(
                    request.PageSize)
                .ToListAsync(
                    cancellationToken);

        IReadOnlyList<AdminIdentityDocumentListItem> items =
            page
                .Select(
                    IdentityDocumentAdminMappings.ToListItem)
                .ToArray();

        return new PagedIdentityDocumentList(
            request.Page,
            request.PageSize,
            totalCount,
            items);
    }
}

public sealed record AdminIdentityDocumentDetail(
    Guid UserId,
    Guid DocumentId,
    string ArabicFullName,
    string EnglishFullName,
    string PhoneNumber,
    string? Email,
    IReadOnlyList<AccountType> AccountTypes,
    UserStatus UserStatus,
    IdentityDocumentVerificationStatus VerificationStatus,
    string? ReviewReason,
    DateTime CreatedOnUtc,
    DateTime UpdatedOnUtc);

public sealed record GetAdminIdentityDocumentQuery(
    UserId UserId)
    : IQuery<AdminIdentityDocumentDetail>;

public sealed class GetAdminIdentityDocumentQueryValidator :
    AbstractValidator<GetAdminIdentityDocumentQuery>
{
    public GetAdminIdentityDocumentQueryValidator()
    {
        RuleFor(query =>
                query.UserId)
            .NotEqual(
                UserId.Empty);
    }
}

public sealed class GetAdminIdentityDocumentQueryHandler :
    IQueryHandler<
        GetAdminIdentityDocumentQuery,
        AdminIdentityDocumentDetail>
{
    private readonly IIdentityDbContext _dbContext;

    public GetAdminIdentityDocumentQueryHandler(
        IIdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<AdminIdentityDocumentDetail>> Handle(
        GetAdminIdentityDocumentQuery request,
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
            return Result<AdminIdentityDocumentDetail>.Failure(
                IdentityDocumentErrors.UserNotFound);
        }

        if (!IdentityDocumentMappings.IsSupportedAccount(
            user))
        {
            return Result<AdminIdentityDocumentDetail>.Failure(
                IdentityDocumentErrors.UnsupportedAccountType);
        }

        if (user.IdentityDocument is null)
        {
            return Result<AdminIdentityDocumentDetail>.Failure(
                IdentityDocumentErrors.NotFound);
        }

        return IdentityDocumentAdminMappings.ToDetail(
            user);
    }
}

public sealed record VerifyIdentityDocumentCommand(
    UserId UserId)
    : ICommand;

public sealed class VerifyIdentityDocumentCommandValidator :
    AbstractValidator<VerifyIdentityDocumentCommand>
{
    public VerifyIdentityDocumentCommandValidator()
    {
        RuleFor(command =>
                command.UserId)
            .NotEqual(
                UserId.Empty);
    }
}

public sealed class VerifyIdentityDocumentCommandHandler :
    ICommandHandler<VerifyIdentityDocumentCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public VerifyIdentityDocumentCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        VerifyIdentityDocumentCommand request,
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
                IdentityDocumentErrors.UserNotFound);
        }

        if (!IdentityDocumentMappings.IsSupportedAccount(
            user))
        {
            return Result.Failure(
                IdentityDocumentErrors.UnsupportedAccountType);
        }

        if (user.IdentityDocument is null)
        {
            return Result.Failure(
                IdentityDocumentErrors.NotFound);
        }

        try
        {
            user.VerifyIdentityDocument(
                _dateTimeProvider.UtcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                IdentityDocumentErrors.InvalidOperation);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}

public sealed record RejectIdentityDocumentCommand(
    UserId UserId,
    string Reason)
    : ICommand;

public sealed class RejectIdentityDocumentCommandValidator :
    AbstractValidator<RejectIdentityDocumentCommand>
{
    public RejectIdentityDocumentCommandValidator()
    {
        RuleFor(command =>
                command.UserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class RejectIdentityDocumentCommandHandler :
    ICommandHandler<RejectIdentityDocumentCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RejectIdentityDocumentCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        RejectIdentityDocumentCommand request,
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
                IdentityDocumentErrors.UserNotFound);
        }

        if (!IdentityDocumentMappings.IsSupportedAccount(
            user))
        {
            return Result.Failure(
                IdentityDocumentErrors.UnsupportedAccountType);
        }

        if (user.IdentityDocument is null)
        {
            return Result.Failure(
                IdentityDocumentErrors.NotFound);
        }

        try
        {
            user.RejectIdentityDocument(
                request.Reason.Trim(),
                _dateTimeProvider.UtcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                IdentityDocumentErrors.InvalidOperation);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}

public sealed record RevokeIdentityDocumentCommand(
    UserId UserId,
    string Reason)
    : ICommand;

public sealed class RevokeIdentityDocumentCommandValidator :
    AbstractValidator<RevokeIdentityDocumentCommand>
{
    public RevokeIdentityDocumentCommandValidator()
    {
        RuleFor(command =>
                command.UserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(command =>
                command.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class RevokeIdentityDocumentCommandHandler :
    ICommandHandler<RevokeIdentityDocumentCommand>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IDateTimeProvider _dateTimeProvider;

    public RevokeIdentityDocumentCommandHandler(
        IIdentityDbContext dbContext,
        IDateTimeProvider dateTimeProvider)
    {
        _dbContext = dbContext;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<Result> Handle(
        RevokeIdentityDocumentCommand request,
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
                IdentityDocumentErrors.UserNotFound);
        }

        if (!IdentityDocumentMappings.IsSupportedAccount(
            user))
        {
            return Result.Failure(
                IdentityDocumentErrors.UnsupportedAccountType);
        }

        if (user.IdentityDocument is null)
        {
            return Result.Failure(
                IdentityDocumentErrors.NotFound);
        }

        DateTime utcNow =
            _dateTimeProvider.UtcNow;

        try
        {
            user.RevokeIdentityDocument(
                request.Reason.Trim(),
                utcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                IdentityDocumentErrors.InvalidOperation);
        }

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
                "National ID was revoked.",
                utcNow);
        }

        await _dbContext.SaveChangesAsync(
            cancellationToken);

        return Result.Success();
    }
}

public sealed record IdentityDocumentFileContent(
    string FileName,
    string ContentType,
    Stream Content);

public sealed record GetIdentityDocumentFileQuery(
    UserId UserId,
    IdentityDocumentFileSide Side)
    : IQuery<IdentityDocumentFileContent>;

public sealed class GetIdentityDocumentFileQueryValidator :
    AbstractValidator<GetIdentityDocumentFileQuery>
{
    public GetIdentityDocumentFileQueryValidator()
    {
        RuleFor(query =>
                query.UserId)
            .NotEqual(
                UserId.Empty);

        RuleFor(query =>
                query.Side)
            .IsInEnum();
    }
}

public sealed class GetIdentityDocumentFileQueryHandler :
    IQueryHandler<
        GetIdentityDocumentFileQuery,
        IdentityDocumentFileContent>
{
    private readonly IIdentityDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public GetIdentityDocumentFileQueryHandler(
        IIdentityDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<IdentityDocumentFileContent>> Handle(
        GetIdentityDocumentFileQuery request,
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
            return Result<IdentityDocumentFileContent>.Failure(
                IdentityDocumentErrors.UserNotFound);
        }

        if (!IdentityDocumentMappings.IsSupportedAccount(
            user))
        {
            return Result<IdentityDocumentFileContent>.Failure(
                IdentityDocumentErrors.UnsupportedAccountType);
        }

        if (user.IdentityDocument is null)
        {
            return Result<IdentityDocumentFileContent>.Failure(
                IdentityDocumentErrors.NotFound);
        }

        string fileKey =
            request.Side ==
                IdentityDocumentFileSide.Front
                ? user.IdentityDocument.FrontImagePath
                : user.IdentityDocument.BackImagePath;

        Result<PrivateFileContent> file =
            await _fileStorage.OpenReadAsync(
                fileKey,
                cancellationToken);

        if (file.IsFailure)
        {
            return Result<IdentityDocumentFileContent>.Failure(
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

        string sideName =
            request.Side ==
                IdentityDocumentFileSide.Front
                ? "front"
                : "back";

        string fileName =
            $"national-id-{user.Id.Value:N}-{sideName}{extension}";

        return new IdentityDocumentFileContent(
            fileName,
            file.Value.ContentType,
            file.Value.Content);
    }
}

internal static class IdentityDocumentAdminMappings
{
    internal static AdminIdentityDocumentListItem ToListItem(
        User user)
    {
        UserIdentityDocument document =
            user.IdentityDocument!;

        return new AdminIdentityDocumentListItem(
            user.Id.Value,
            document.Id.Value,
            user.ArabicFullName.Value,
            user.EnglishFullName.Value,
            user.PhoneNumber.Value,
            user.Email?.Value,
            user.Accounts
                .Select(account =>
                    account.AccountType)
                .ToArray(),
            user.Status,
            document.VerificationStatus,
            document.ReviewReason,
            document.UpdatedOnUtc);
    }

    internal static AdminIdentityDocumentDetail ToDetail(
        User user)
    {
        UserIdentityDocument document =
            user.IdentityDocument!;

        return new AdminIdentityDocumentDetail(
            user.Id.Value,
            document.Id.Value,
            user.ArabicFullName.Value,
            user.EnglishFullName.Value,
            user.PhoneNumber.Value,
            user.Email?.Value,
            user.Accounts
                .Select(account =>
                    account.AccountType)
                .ToArray(),
            user.Status,
            document.VerificationStatus,
            document.ReviewReason,
            document.CreatedOnUtc,
            document.UpdatedOnUtc);
    }
}

file static class IdentityDocumentAdminQueries
{
    internal static Task<User?> LoadSupportedUserWithDocumentAsync(
        IIdentityDbContext dbContext,
        UserId userId,
        CancellationToken cancellationToken)
    {
        return dbContext.Users
            .SingleOrDefaultAsync(
                item =>
                    item.Id ==
                    userId,
                cancellationToken);
    }
}
