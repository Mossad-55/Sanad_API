using System.Security.Cryptography;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Application.Abstractions.Data;
using Sanad.Modules.Identity.Application.Abstractions.Messaging;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Authentication.Registration;
using Sanad.Modules.Identity.Application.Authentication;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;
using Sanad.Modules.Identity.Domain.Authentication.VerificationRequests;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.Modules.Identity.Application.Users.Admin;

public static partial class AdminUserErrors
{
    public static Error InvalidPassword => new("Identity.Admin.InvalidPassword", "Password does not meet policy.");
    public static Error IllegalAccountTypeTransition => new("Identity.Admin.IllegalAccountTypeTransition", "The account type transition is not allowed.");
}

public sealed record ResetAdminUserPasswordCommand(UserId ActorUserId, UserId TargetUserId) : ICommand<ResetAdminUserPasswordResponse>;
public sealed record ResetAdminUserPasswordResponse(Guid UserId, string TemporaryPassword, int RevokedSessionCount);
public sealed record ChangeAdminUserAccountTypeCommand(UserId ActorUserId, UserId TargetUserId, AccountType AccountType, string? Reason = null) : ICommand<AdminUserAccountTypeResponse>;
public sealed record AdminUserAccountTypeResponse(Guid UserId, AccountType AccountType);
public sealed record CreateAdminUserCommand(UserId ActorUserId, string ArabicFullName, string EnglishFullName, string Email, string PhoneNumber, string Password, AccountType AccountType) : ICommand<RegisterUserResponse>;

public sealed class ResetAdminUserPasswordCommandValidator : AbstractValidator<ResetAdminUserPasswordCommand>
{
    public ResetAdminUserPasswordCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(UserId.Empty);
    }
}

public sealed class ChangeAdminUserAccountTypeCommandValidator : AbstractValidator<ChangeAdminUserAccountTypeCommand>
{
    public ChangeAdminUserAccountTypeCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.TargetUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.AccountType).Must(IsAdministrativeType);
        RuleFor(x => x.Reason).MaximumLength(1000);
    }

    public static bool IsAdministrativeType(AccountType type) => type is AccountType.SuperAdmin or AccountType.SupportAdmin or AccountType.ContentAdmin or AccountType.FinanceAdmin;
}

public sealed class CreateAdminUserCommandValidator : AbstractValidator<CreateAdminUserCommand>
{
    public CreateAdminUserCommandValidator()
    {
        RuleFor(x => x.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(x => x.ArabicFullName).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.EnglishFullName).NotEmpty().MinimumLength(2).MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().MaximumLength(256).EmailAddress();
        RuleFor(x => x.PhoneNumber).NotEmpty().Matches(@"\A\+[1-9][0-9]{1,14}\z");
        RuleFor(x => x.Password).NotEmpty().MinimumLength(10).MaximumLength(128).Matches("[A-Z]").Matches("[a-z]").Matches("[0-9]");
        RuleFor(x => x.AccountType).Must(ChangeAdminUserAccountTypeCommandValidator.IsAdministrativeType);
    }
}

public sealed class ResetAdminUserPasswordCommandHandler(IIdentityDbContext db, IPasswordHasher hasher, IDateTimeProvider clock, IAdminUserSeedProtection seedProtection, IAdminUserStatusNoticePublisher notices)
    : ICommandHandler<ResetAdminUserPasswordCommand, ResetAdminUserPasswordResponse>
{
    public async Task<Result<ResetAdminUserPasswordResponse>> Handle(ResetAdminUserPasswordCommand request, CancellationToken ct)
    {
        User? user = await db.Users.Include(x => x.Accounts).SingleOrDefaultAsync(x => x.Id == request.TargetUserId, ct);
        if (user is null) return AdminUserErrors.NotFound;
        if (seedProtection.IsSeeded(user)) return AdminUserErrors.SeededProtected;

        string temporaryPassword = $"{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}aZ7!";
        DateTime now = clock.UtcNow;
        user.ResetPasswordHash(hasher.Hash(temporaryPassword), now);
        DeviceSession[] sessions = await db.DeviceSessions.Where(x => x.UserId == user.Id && x.RevokedOnUtc == null).ToArrayAsync(ct);
        foreach (DeviceSession session in sessions) session.Revoke("Admin reset the user's password.", now);
        db.AdminUserActions.Add(AdminUserAction.Create(request.ActorUserId, user.Id, AdminUserActionType.PasswordReset, null, now));
        await db.SaveChangesAsync(ct);
        await notices.PublishAsync(new(user.Id, user.Email?.Value, user.ArabicFullName.Value, user.EnglishFullName.Value, user.Status, now, "PasswordReset"), ct);
        return new ResetAdminUserPasswordResponse(user.Id.Value, temporaryPassword, sessions.Length);
    }
}

public sealed class ChangeAdminUserAccountTypeCommandHandler(IIdentityDbContext db, IDateTimeProvider clock, IAdminUserSeedProtection seedProtection)
    : ICommandHandler<ChangeAdminUserAccountTypeCommand, AdminUserAccountTypeResponse>
{
    public async Task<Result<AdminUserAccountTypeResponse>> Handle(ChangeAdminUserAccountTypeCommand request, CancellationToken ct)
    {
        if (!ChangeAdminUserAccountTypeCommandValidator.IsAdministrativeType(request.AccountType)) return AdminUserErrors.IllegalAccountTypeTransition;
        User? user = await db.Users.Include(x => x.Accounts).SingleOrDefaultAsync(x => x.Id == request.TargetUserId, ct);
        if (user is null) return AdminUserErrors.NotFound;
        if (seedProtection.IsSeeded(user)) return AdminUserErrors.SeededProtected;
        if (request.ActorUserId == request.TargetUserId) return AdminUserErrors.SelfAction;
        AccountType? current = user.Accounts.Select(x => x.AccountType).Where(ChangeAdminUserAccountTypeCommandValidator.IsAdministrativeType).Select(x => (AccountType?)x).SingleOrDefault();
        if (current is null || current == request.AccountType) return AdminUserErrors.IllegalAccountTypeTransition;
        if (current == AccountType.SuperAdmin && user.Status == UserStatus.Active && request.AccountType != AccountType.SuperAdmin &&
            await db.Users.CountAsync(x => x.Status == UserStatus.Active && x.Accounts.Any(a => a.AccountType == AccountType.SuperAdmin), ct) <= 1)
            return AdminUserErrors.LastSuperAdmin;
        user.ChangeAdministrativeAccount(request.AccountType, clock.UtcNow);
        db.AdminUserActions.Add(AdminUserAction.Create(request.ActorUserId, user.Id, AdminUserActionType.AccountTypeChanged, request.Reason, clock.UtcNow));
        await db.SaveChangesAsync(ct);
        return new AdminUserAccountTypeResponse(user.Id.Value, request.AccountType);
    }
}

public sealed class CreateAdminUserCommandHandler(IIdentityDbContext db, IPasswordHasher hasher, IOtpService otp, IEmailSender emailSender, ISmsSender smsSender, IDateTimeProvider clock)
    : ICommandHandler<CreateAdminUserCommand, RegisterUserResponse>
{
    public async Task<Result<RegisterUserResponse>> Handle(CreateAdminUserCommand request, CancellationToken ct)
    {
        if (!ChangeAdminUserAccountTypeCommandValidator.IsAdministrativeType(request.AccountType)) return AdminUserErrors.IllegalAccountTypeTransition;
        Email email = Email.Create(request.Email);
        PhoneNumber phone = PhoneNumber.Create(request.PhoneNumber);
        if (await db.Users.AnyAsync(x => x.Email == email, ct)) return RegistrationErrors.EmailAlreadyInUse;
        if (await db.Users.AnyAsync(x => x.PhoneNumber == phone, ct)) return RegistrationErrors.PhoneAlreadyInUse;

        DateTime now = clock.UtcNow;
        User user = User.Create(FullName.Create(request.ArabicFullName), FullName.Create(request.EnglishFullName), email, phone);
        user.AddAccount(request.AccountType);
        user.SetInitialPasswordHash(hasher.Hash(request.Password), now);
        GeneratedOtpCode emailCode = otp.Generate(OtpPolicy.CodeLength);
        GeneratedOtpCode phoneCode = otp.Generate(OtpPolicy.CodeLength);
        DateTime expires = now.Add(OtpPolicy.Lifetime);
        VerificationRequest emailVerification = VerificationRequest.Create(user.Id, email.Value, emailCode.Hash, VerificationChannel.Email, VerificationPurpose.VerifyEmail, now, expires);
        VerificationRequest phoneVerification = VerificationRequest.Create(user.Id, phone.Value, phoneCode.Hash, VerificationChannel.Sms, VerificationPurpose.VerifyPhone, now, expires);
        db.Users.Add(user);
        db.AdminUserActions.Add(AdminUserAction.Create(request.ActorUserId, user.Id, AdminUserActionType.Created, request.AccountType.ToString(), now));
        db.VerificationRequests.AddRange(emailVerification, phoneVerification);
        await db.SaveChangesAsync(ct);
        await Task.WhenAll(
            emailSender.SendVerificationCodeAsync(email.Value, emailCode.PlainTextCode, VerificationPurpose.VerifyEmail, ct),
            smsSender.SendVerificationCodeAsync(phone.Value, phoneCode.PlainTextCode, VerificationPurpose.VerifyPhone, ct));
        return new RegisterUserResponse(user.Id, emailVerification.Id, phoneVerification.Id);
    }
}
