using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserStatusTransitionTests
{
    [Fact]
    public void Activate_ShouldAllowVerifiedElderlyWithoutEmailOrPassword()
    {
        User user =
            CreateUserWithoutEmail();

        user.AddAccount(
            AccountType.Elderly);

        user.VerifyPhone(
            CreateUtcDateTime());

        user.ClearDomainEvents();

        user.Activate(
            CreateUtcDateTime());

        Assert.Equal(
            UserStatus.Active,
            user.Status);

        Assert.Null(user.StatusReason);

        UserStatusChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserStatusChangedDomainEvent>());

        Assert.Equal(
            UserStatus.PendingVerification,
            domainEvent.PreviousStatus);

        Assert.Equal(
            UserStatus.Active,
            domainEvent.CurrentStatus);
    }

    [Fact]
    public void Activate_ShouldAllowVerifiedFamilyWithPassword()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.Family);

        user.VerifyEmail(
            CreateUtcDateTime());
        user.VerifyPhone(
            CreateUtcDateTime());

        user.SetInitialPasswordHash(
            "password-hash",
            CreateUtcDateTime());

        user.Activate(
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.Equal(
            UserStatus.Active,
            user.Status);
    }

    [Fact]
    public void Activate_ShouldAllowVerifiedFamilyWithExternalLogin()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.Family);

        user.VerifyEmail(
            CreateUtcDateTime());
        user.VerifyPhone(
            CreateUtcDateTime());

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "google-subject",
            CreateUtcDateTime());

        user.Activate(
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.Equal(
            UserStatus.Active,
            user.Status);
    }

    [Fact]
    public void Activate_ShouldRejectUserWithoutAccount()
    {
        User user =
            CreateUser();

        user.VerifyEmail(
            CreateUtcDateTime());
        user.VerifyPhone(
            CreateUtcDateTime());

        user.SetInitialPasswordHash(
            "password-hash",
            CreateUtcDateTime());

        Assert.Throws<DomainException>(
            () => user.Activate(
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);
    }

    [Fact]
    public void Activate_ShouldRejectNonElderlyWithoutVerifiedEmail()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.MedicalCaregiver);

        user.VerifyPhone(
            CreateUtcDateTime());

        user.SetInitialPasswordHash(
            "password-hash",
            CreateUtcDateTime());

        Assert.Throws<DomainException>(
            () => user.Activate(
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);
    }

    [Fact]
    public void Activate_ShouldRejectNonElderlyWithoutAuthenticationMethod()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.CompanionCaregiver);

        user.VerifyEmail(
            CreateUtcDateTime());
        user.VerifyPhone(
            CreateUtcDateTime());

        Assert.Throws<DomainException>(
            () => user.Activate(
                CreateUtcDateTime()));

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);
    }

    [Fact]
    public void AddAccount_ShouldRejectInvalidAccountType()
    {
        User user =
            CreateUser();

        Assert.Throws<DomainException>(
            () => user.AddAccount(
                (AccountType)999));

        Assert.Empty(user.Accounts);
    }

    [Fact]
    public void Suspend_ShouldRequireReasonAndRaiseEvent()
    {
        User user =
            CreateActiveFamilyUser();

        user.ClearDomainEvents();

        user.Suspend(
            "  Security review required.  ",
            CreateUtcDateTime()
                .AddMinutes(2));

        Assert.Equal(
            UserStatus.Suspended,
            user.Status);

        Assert.Equal(
            "Security review required.",
            user.StatusReason);

        UserStatusChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserStatusChangedDomainEvent>());

        Assert.Equal(
            UserStatus.Active,
            domainEvent.PreviousStatus);

        Assert.Equal(
            UserStatus.Suspended,
            domainEvent.CurrentStatus);
    }

    [Fact]
    public void Reactivate_ShouldRequireReadinessAndRemainReasonless()
    {
        User user =
            CreateActiveFamilyUser();

        user.Suspend(
            "Temporary suspension.",
            CreateUtcDateTime()
                .AddMinutes(2));

        user.ClearDomainEvents();

        user.Reactivate(
            CreateUtcDateTime()
                .AddMinutes(3));

        Assert.Equal(
            UserStatus.Active,
            user.Status);

        Assert.Null(user.StatusReason);
    }

    [Fact]
    public void Block_ShouldBlockPendingUserWithReason()
    {
        User user =
            CreateUser();

        user.Block(
            "  Fraud investigation.  ",
            CreateUtcDateTime());

        Assert.Equal(
            UserStatus.Blocked,
            user.Status);

        Assert.Equal(
            "Fraud investigation.",
            user.StatusReason);
    }

    [Fact]
    public void Unblock_ShouldReturnToPendingAndClearVerification()
    {
        User user =
            CreateActiveFamilyUser();

        user.Block(
            "Security block.",
            CreateUtcDateTime()
                .AddMinutes(2));

        user.Unblock(
            CreateUtcDateTime()
                .AddMinutes(3));

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        Assert.Null(user.StatusReason);

        Assert.False(user.EmailVerified);
        Assert.False(user.PhoneVerified);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Suspend_ShouldRejectMissingReason(
        string? reason)
    {
        User user =
            CreateActiveFamilyUser();

        Assert.Throws<DomainException>(
            () => user.Suspend(
                reason!,
                CreateUtcDateTime()
                    .AddMinutes(2)));

        Assert.Equal(
            UserStatus.Active,
            user.Status);
    }

    private static User CreateActiveFamilyUser()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.Family);

        user.VerifyEmail(
            CreateUtcDateTime());
        user.VerifyPhone(
            CreateUtcDateTime());

        user.SetInitialPasswordHash(
            "password-hash",
            CreateUtcDateTime());

        user.Activate(
            CreateUtcDateTime()
                .AddMinutes(1));

        return user;
    }

    private static User CreateUser()
    {
        return CreateUserWithEmail(
            Email.Create(
                "mohamed@example.com"));
    }

    private static User CreateUserWithoutEmail()
    {
        return CreateUserWithEmail(
            email: null);
    }

    private static User CreateUserWithEmail(
        Email? email)
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            email,
            PhoneNumber.Create(
                "+201001234567"));
    }

    private static DateTime CreateUtcDateTime()
    {
        return new DateTime(
            2026,
            8,
            20,
            10,
            0,
            0,
            DateTimeKind.Utc);
    }
}