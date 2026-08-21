using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users.Events;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.Modules.Identity.Domain.Authentication;

namespace Sanad.Modules.Identity.Domain.Users;

public sealed class User : AggregateRoot<UserId>
{
    private readonly List<UserAccount> _accounts = [];

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

    public IReadOnlyCollection<UserAccount> Accounts => _accounts.AsReadOnly();

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
        if (_accounts.Any(a => a.AccountType == accountType))
        {
            throw new DomainException(
                UserErrors.UserAlreadyHasAccount);
        }

        _accounts.Add(
            UserAccount.Create(accountType));

        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void VerifyEmail()
    {
        if (Email is null)
        {
            throw new DomainException(UserErrors.EmailNotSet);
        }

        EmailVerified = true;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void VerifyPhone()
    {
        PhoneVerified = true;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void ChangeEmail(Email email)
    {
        Email = email;
        EmailVerified = false;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void ChangeAvatar(string? avatarUrl)
    {
        AvatarUrl = avatarUrl;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void ChangePhoneNumber(PhoneNumber phoneNumber)
    {
        PhoneNumber = phoneNumber;
        PhoneVerified = false;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UpdateLastLogin()
    {
        LastLoginOnUtc = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (!PhoneVerified)
        {
            throw new DomainException("Phone number must be verified.");
        }

        Status = UserStatus.Active;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Suspend()
    {
        Status = UserStatus.Suspended;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void Block()
    {
        Status = UserStatus.Blocked;
        UpdatedOnUtc = DateTime.UtcNow;
    }

    public void UploadIdentityDocument(
        string frontImagePath,
        string backImagePath)
    {
        IdentityDocument =
            UserIdentityDocument.Create(
                frontImagePath,
                backImagePath);
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
}