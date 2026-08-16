using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Identity.Domain.Users.Events;
using Sanad.Modules.Identity.Domain.ValueObjects;

namespace Sanad.Modules.Identity.Domain.Users;

public sealed class User : AggregateRoot<Guid>
{
    private readonly List<UserAccount> _accounts = [];

    private User()
    {
    }

    private User(
        Guid id,
        FullName fullName,
        Email? email,
        PhoneNumber phoneNumber,
        string? avatarUrl,
        UserStatus status,
        DateTime createdOnUtc)
        : base(id)
    {
        FullName = fullName;
        Email = email;
        PhoneNumber = phoneNumber;
        AvatarUrl = avatarUrl;
        Status = status;

        EmailVerified = false;
        PhoneVerified = false;

        CreatedOnUtc = createdOnUtc;
        UpdatedOnUtc = createdOnUtc;
    }

    public FullName FullName { get; private set; }

    public Email? Email { get; private set; }

    public PhoneNumber PhoneNumber { get; private set; }

    public string? AvatarUrl { get; private set; }

    public bool EmailVerified { get; private set; }

    public bool PhoneVerified { get; private set; }

    public UserStatus Status { get; private set; }

    public DateTime CreatedOnUtc { get; private set; }

    public DateTime UpdatedOnUtc { get; private set; }

    public DateTime? LastLoginOnUtc { get; private set; }

    public IReadOnlyCollection<UserAccount> Accounts =>
        _accounts.AsReadOnly();

    public static User Create(
        FullName fullName,
        Email? email,
        PhoneNumber phoneNumber,
        string? avatarUrl = null)
    {
        var user = new User(
            Guid.CreateVersion7(),
            fullName,
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
        if(Email is null)
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
}