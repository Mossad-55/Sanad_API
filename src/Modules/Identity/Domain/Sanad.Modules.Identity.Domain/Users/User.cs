using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users.Events;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.Identity.Domain.Authentication;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;

namespace Sanad.Modules.Identity.Domain.Users;

public sealed class User : AggregateRoot<UserId>
{
    public const int MaximumStatusReasonLength = 1000;

    private readonly List<UserAccount> _accounts = [];
    private readonly List<UserExternalLogin> _externalLogins = [];

    private User()
    {
    }

    private User(
        UserId id,
        FullName arabicFullName,
        FullName englishFullName,
        Email? email,
        PhoneNumber phoneNumber,
        string? avatarUrl,
        UserStatus status,
        DateTime createdOnUtc)
        : base(id)
    {
        ArabicFullName = arabicFullName;
        EnglishFullName = englishFullName;
        Email = email;
        PhoneNumber = phoneNumber;
        AvatarUrl = avatarUrl;
        Status = status;

        EmailVerified = false;
        PhoneVerified = false;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public FullName ArabicFullName { get; private set; } = default!;
    public FullName EnglishFullName { get; private set; } = default!;
    public DateOnly? DateOfBirth { get; private set; }
    public Gender? Gender { get; private set; }
    public Email? Email { get; private set; }
    public PhoneNumber PhoneNumber { get; private set; } = default!;
    public PasswordCredential? Password { get; private set; }
    public bool HasPassword => Password is not null;
    public UserIdentityDocument? IdentityDocument { get; private set; }
    public string? AvatarUrl { get; private set; }
    public bool EmailVerified { get; private set; }
    public bool PhoneVerified { get; private set; }
    public UserStatus Status { get; private set; }
    public DateTime CreatedOnUtc { get; private set; }
    public DateTime UpdatedOnUtc { get; private set; }
    public DateTime? LastLoginOnUtc { get; private set; }
    public bool HasExternalLogin => _externalLogins.Count > 0;
    public string? StatusReason { get; private set; }

    public IReadOnlyCollection<UserAccount> Accounts => _accounts.AsReadOnly();
    public IReadOnlyCollection<UserExternalLogin> ExternalLogins => _externalLogins.AsReadOnly();

    public static User Create(
        FullName arabicFullName,
        FullName englishFullName,
        Email? email,
        PhoneNumber phoneNumber,
        string? avatarUrl = null)
    {
        var user = new User(
            UserId.New(),
            arabicFullName,
            englishFullName,
            email,
            phoneNumber,
            avatarUrl,
            UserStatus.PendingVerification,
            DateTime.UtcNow);

        user.RaiseDomainEvent(
            new UserRegisteredDomainEvent(user.Id));

        return user;
    }

    public void AddAccount(AccountType accountType)
    {
        if (!Enum.IsDefined(accountType))
        {
            throw new DomainException(
                "Account type is invalid.");
        }

        if (_accounts.Any(a => a.AccountType == accountType))
        {
            throw new DomainException(
                UserErrors.UserAlreadyHasAccount);
        }

        _accounts.Add(
            UserAccount.Create(accountType));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void VerifyEmail(
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (Email is null)
        {
            throw new DomainException(UserErrors.EmailNotSet);
        }

        if (EmailVerified)
        {
            return;
        }

        EmailVerified = true;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(new UserContactVerifiedDomainEvent(
            Id,
            UserContactType.Email));
    }

    public void VerifyPhone(
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (PhoneVerified)
        {
            return;
        }

        PhoneVerified = true;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserContactVerifiedDomainEvent(
                Id,
                UserContactType.Phone));
    }

    public void ChangeEmail(
        Email email,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(email);
        ValidateUtc(utcNow);
        EnsureNotBlocked();

        if (Email == email)
        {
            return;
        }

        UserStatus previousStatus =
            Status;

        Email = email;
        EmailVerified = false;

        if (Status == UserStatus.Active)
        {
            Status =
                UserStatus.PendingVerification;

            StatusReason = null;
        }

        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserContactChangedDomainEvent(
                Id,
                UserContactType.Email));

        if (previousStatus != Status)
        {
            RaiseDomainEvent(
                new UserStatusChangedDomainEvent(
                    Id,
                    previousStatus,
                    Status,
                    Reason: null));
        }
    }

    public void ChangeAvatar(string? avatarUrl)
    {
        AvatarUrl = avatarUrl;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void ChangePhoneNumber(
        PhoneNumber phoneNumber,
        DateTime utcNow)
    {
        ArgumentNullException.ThrowIfNull(
            phoneNumber);

        ValidateUtc(utcNow);
        EnsureNotBlocked();

        if (PhoneNumber == phoneNumber)
        {
            return;
        }

        UserStatus previousStatus =
            Status;

        PhoneNumber = phoneNumber;
        PhoneVerified = false;

        if (Status == UserStatus.Active)
        {
            Status =
                UserStatus.PendingVerification;

            StatusReason = null;
        }

        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserContactChangedDomainEvent(
                Id,
                UserContactType.Phone));

        if (previousStatus != Status)
        {
            RaiseDomainEvent(
                new UserStatusChangedDomainEvent(
                    Id,
                    previousStatus,
                    Status,
                    Reason: null));
        }
    }

    public void UpdateLastLogin(
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        LastLoginOnUtc = utcNow;
        UpdatedOnUtc = utcNow;
    }

    public void Activate(
        DateTime utcNow)
    {
        EnsureStatus(
            UserStatus.PendingVerification,
            "Only a Pending Verification User can be activated.");

        ValidateUtc(utcNow);
        ValidateActivationReadiness();

        UserStatus previousStatus =
            Status;

        Status = UserStatus.Active;
        StatusReason = null;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserStatusChangedDomainEvent(
                Id,
                previousStatus,
                Status,
                Reason: null));
    }

    public void Suspend(
        string reason,
        DateTime utcNow)
    {
        EnsureStatus(
            UserStatus.Active,
            "Only an Active User can be suspended.");

        ValidateUtc(utcNow);

        string normalizedReason =
            NormalizeRequiredStatusReason(
                reason,
                "Suspension reason");

        UserStatus previousStatus =
            Status;

        Status = UserStatus.Suspended;
        StatusReason = normalizedReason;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserStatusChangedDomainEvent(
                Id,
                previousStatus,
                Status,
                normalizedReason));
    }

    public void Block(
        string reason,
        DateTime utcNow)
    {
        if (Status == UserStatus.Blocked)
        {
            throw new DomainException(
                "User is already Blocked.");
        }

        ValidateUtc(utcNow);

        string normalizedReason =
            NormalizeRequiredStatusReason(
                reason,
                "Block reason");

        UserStatus previousStatus =
            Status;

        Status = UserStatus.Blocked;
        StatusReason = normalizedReason;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserStatusChangedDomainEvent(
                Id,
                previousStatus,
                Status,
                normalizedReason));
    }

    public void Reactivate(
        DateTime utcNow)
    {
        EnsureStatus(
            UserStatus.Suspended,
            "Only a Suspended User can be reactivated.");

        ValidateUtc(utcNow);
        ValidateActivationReadiness();

        UserStatus previousStatus =
            Status;

        Status = UserStatus.Active;
        StatusReason = null;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserStatusChangedDomainEvent(
                Id,
                previousStatus,
                Status,
                Reason: null));
    }

    public void Unblock(
        DateTime utcNow)
    {
        EnsureStatus(
            UserStatus.Blocked,
            "Only a Blocked User can be unblocked.");

        ValidateUtc(utcNow);

        UserStatus previousStatus =
            Status;

        Status =
            UserStatus.PendingVerification;

        StatusReason = null;
        EmailVerified = false;
        PhoneVerified = false;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserStatusChangedDomainEvent(
                Id,
                previousStatus,
                Status,
                Reason: null));
    }

    public void UpdateIdentityDocument(
        string frontImagePath,
        string backImagePath)
    {
        if (IdentityDocument is null)
        {
            throw new DomainException(
                "Identity document does not exist.");
        }

        IdentityDocument.UpdateImages(
            frontImagePath,
            backImagePath);
    }

    public void CompletePersonalInformation(
        DateOnly dateOfBirth,
        Gender gender,
        DateOnly currentDate)
    {
        if (DateOfBirth.HasValue ||
            Gender.HasValue)
        {
            throw new DomainException(
                "Personal information has already been completed.");
        }

        ValidateDateOfBirth(
            dateOfBirth,
            currentDate);

        ValidateGender(gender);

        DateOfBirth = dateOfBirth;
        Gender = gender;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void ChangeGender(
        Gender gender)
    {
        EnsurePersonalInformationCompleted();
        ValidateGender(gender);

        Gender = gender;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void CorrectDateOfBirth(
        DateOnly dateOfBirth,
        DateOnly currentDate)
    {
        EnsurePersonalInformationCompleted();

        ValidateDateOfBirth(
            dateOfBirth,
            currentDate);

        DateOfBirth = dateOfBirth;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void SetInitialPasswordHash(
        string passwordHash,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (Password is not null)
        {
            throw new DomainException(
                "User already has a password.");
        }

        PasswordCredential credential =
            PasswordCredential.Create(
                passwordHash);

        Password = credential;
        UpdatedOnUtc = utcNow;
    }

    public void ChangePasswordHash(
        string passwordHash,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (Password is null)
        {
            throw new DomainException(
                "User does not have a password.");
        }

        PasswordCredential credential =
            PasswordCredential.Create(
                passwordHash);

        Password = credential;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserPasswordChangedDomainEvent(
                Id,
                PasswordChangeReason.Changed));
    }

    public void ResetPasswordHash(
        string passwordHash,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        PasswordCredential credential =
            PasswordCredential.Create(
                passwordHash);

        Password = credential;
        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserPasswordChangedDomainEvent(
                Id,
                PasswordChangeReason.Reset));
    }

    public void LinkExternalLogin(
        ExternalLoginProvider provider,
        string providerSubject,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (!Enum.IsDefined(provider))
        {
            throw new DomainException(
                "External login provider is invalid.");
        }

        bool providerAlreadyLinked =
            _externalLogins.Any(
                externalLogin =>
                    externalLogin.Provider ==
                    provider);

        if (providerAlreadyLinked)
        {
            throw new DomainException(
                "This external login provider is " +
                "already linked.");
        }

        UserExternalLogin externalLogin =
            UserExternalLogin.Create(
                provider,
                providerSubject,
                utcNow);

        _externalLogins.Add(
            externalLogin);

        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserExternalLoginLinkedDomainEvent(
                Id,
                externalLogin.Id,
                provider));
    }

    public void UnlinkExternalLogin(
        ExternalLoginProvider provider,
        DateTime utcNow)
    {
        ValidateUtc(utcNow);

        if (!Enum.IsDefined(provider))
        {
            throw new DomainException(
                "External login provider is invalid.");
        }

        UserExternalLogin? externalLogin =
            _externalLogins.SingleOrDefault(
                externalLogin =>
                    externalLogin.Provider ==
                    provider);

        if (externalLogin is null)
        {
            throw new DomainException(
                "External login provider is not linked.");
        }

        bool wouldRemoveLastAuthenticationMethod =
            Password is null &&
            _externalLogins.Count == 1;

        if (wouldRemoveLastAuthenticationMethod)
        {
            throw new DomainException(
                "Cannot remove the final authentication method.");
        }

        _externalLogins.Remove(
            externalLogin);

        UpdatedOnUtc = utcNow;

        RaiseDomainEvent(
            new UserExternalLoginUnlinkedDomainEvent(
                Id,
                provider));
    }

    internal void ValidateActivationReadiness()
    {
        if (_accounts.Count == 0)
        {
            throw new DomainException(
                "User must have at least one account.");
        }

        if (!PhoneVerified)
        {
            throw new DomainException(
                "Phone number must be verified.");
        }

        bool hasNonElderlyAccount =
            _accounts.Any(
                account =>
                    account.AccountType !=
                    AccountType.Elderly);

        if (!hasNonElderlyAccount)
        {
            return;
        }

        if (Email is null)
        {
            throw new DomainException(
                "Email is required.");
        }

        if (!EmailVerified)
        {
            throw new DomainException(
                "Email must be verified.");
        }

        bool hasAuthenticationMethod =
            HasPassword ||
            HasExternalLogin;

        if (!hasAuthenticationMethod)
        {
            throw new DomainException(
                "Password or external login is required.");
        }
    }

    private void EnsurePersonalInformationCompleted()
    {
        if (!DateOfBirth.HasValue ||
            !Gender.HasValue)
        {
            throw new DomainException(
                "Personal information is not complete.");
        }
    }

    private static void ValidateDateOfBirth(
        DateOnly dateOfBirth,
        DateOnly currentDate)
    {
        if (dateOfBirth > currentDate)
        {
            throw new DomainException(
                "Date of birth cannot be in the future.");
        }
    }

    private static void ValidateGender(
        Gender gender)
    {
        if (!Enum.IsDefined(gender))
        {
            throw new DomainException(
                "Gender is invalid.");
        }
    }

    private static void ValidateUtc(
        DateTime utcNow)
    {
        if (utcNow.Kind != DateTimeKind.Utc)
        {
            throw new DomainException(
                "Operation time must be in UTC.");
        }
    }

    private void EnsureStatus(
    UserStatus requiredStatus,
    string errorMessage)
    {
        if (Status != requiredStatus)
        {
            throw new DomainException(
                errorMessage);
        }
    }

    private static string NormalizeRequiredStatusReason(
        string reason,
        string fieldName)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException(
                $"{fieldName} is required.");
        }

        string normalizedReason =
            reason.Trim();

        if (normalizedReason.Length >
            MaximumStatusReasonLength)
        {
            throw new DomainException(
                $"{fieldName} cannot exceed " +
                $"{MaximumStatusReasonLength} characters.");
        }

        return normalizedReason;
    }

    private void EnsureNotBlocked()
    {
        if (Status == UserStatus.Blocked)
        {
            throw new DomainException(
                "Blocked User information cannot be changed.");
        }
    }
}