using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserContactSecurityTests
{
    [Fact]
    public void VerifyEmail_ShouldRaiseVerifiedEvent()
    {
        User user =
            CreateUser();

        user.ClearDomainEvents();

        DateTime utcNow =
            CreateUtcDateTime();

        user.VerifyEmail(utcNow);

        Assert.True(user.EmailVerified);
        Assert.Equal(utcNow, user.UpdatedOnUtc);

        UserContactVerifiedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserContactVerifiedDomainEvent>());

        Assert.Equal(
            UserContactType.Email,
            domainEvent.ContactType);
    }

    [Fact]
    public void VerifyPhone_ShouldRaiseVerifiedEvent()
    {
        User user =
            CreateUser();

        user.ClearDomainEvents();

        user.VerifyPhone(
            CreateUtcDateTime());

        Assert.True(user.PhoneVerified);

        UserContactVerifiedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserContactVerifiedDomainEvent>());

        Assert.Equal(
            UserContactType.Phone,
            domainEvent.ContactType);
    }

    [Fact]
    public void VerifyContact_ShouldBeIdempotent()
    {
        User user =
            CreateUser();

        user.VerifyEmail(
            CreateUtcDateTime());

        DateTime originalUpdatedOnUtc =
            user.UpdatedOnUtc;

        user.ClearDomainEvents();

        user.VerifyEmail(
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.Equal(
            originalUpdatedOnUtc,
            user.UpdatedOnUtc);

        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void ChangeEmail_ShouldReturnActiveUserToPendingVerification()
    {
        User user =
            CreateActiveFamilyUser();

        user.ClearDomainEvents();

        DateTime changedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(2);

        user.ChangeEmail(
            Email.Create(
                "new@example.com"),
            changedOnUtc);

        Assert.Equal(
            "new@example.com",
            user.Email!.Value);

        Assert.False(user.EmailVerified);
        Assert.True(user.PhoneVerified);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        Assert.Equal(
            changedOnUtc,
            user.UpdatedOnUtc);

        Assert.Single(
            user.DomainEvents
                .OfType<
                    UserContactChangedDomainEvent>());

        Assert.Single(
            user.DomainEvents
                .OfType<
                    UserStatusChangedDomainEvent>());
    }

    [Fact]
    public void ChangePhone_ShouldReturnActiveUserToPendingVerification()
    {
        User user =
            CreateActiveFamilyUser();

        user.ClearDomainEvents();

        user.ChangePhoneNumber(
            PhoneNumber.Create(
                "+201009876543"),
            CreateUtcDateTime()
                .AddMinutes(2));

        Assert.Equal(
            "+201009876543",
            user.PhoneNumber.Value);

        Assert.False(user.PhoneVerified);
        Assert.True(user.EmailVerified);

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        UserContactChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserContactChangedDomainEvent>());

        Assert.Equal(
            UserContactType.Phone,
            domainEvent.ContactType);
    }

    [Fact]
    public void ChangeEmail_ShouldDoNothing_WhenEmailIsUnchanged()
    {
        User user =
            CreateUser();

        DateTime originalUpdatedOnUtc =
            user.UpdatedOnUtc;

        user.ClearDomainEvents();

        user.ChangeEmail(
            Email.Create(
                "mohamed@example.com"),
            CreateUtcDateTime());

        Assert.Equal(
            originalUpdatedOnUtc,
            user.UpdatedOnUtc);

        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void ChangeContact_ShouldRejectBlockedUser()
    {
        User user =
            CreateUser();

        user.Block(
            "Security block.",
            CreateUtcDateTime());

        Assert.Throws<DomainException>(
            () => user.ChangeEmail(
                Email.Create(
                    "new@example.com"),
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            UserStatus.Blocked,
            user.Status);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void ChangeEmail_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        User user =
            CreateUser();

        DateTime invalidTime =
            DateTime.SpecifyKind(
                CreateUtcDateTime(),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => user.ChangeEmail(
                Email.Create(
                    "new@example.com"),
                invalidTime));

        Assert.Equal(
            "mohamed@example.com",
            user.Email!.Value);
    }

    [Fact]
    public void UpdateLastLogin_ShouldUseProvidedTime()
    {
        User user =
            CreateUser();

        DateTime loginOnUtc =
            CreateUtcDateTime();

        user.UpdateLastLogin(
            loginOnUtc);

        Assert.Equal(
            loginOnUtc,
            user.LastLoginOnUtc);

        Assert.Equal(
            loginOnUtc,
            user.UpdatedOnUtc);
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
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create(
                "mohamed@example.com"),
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