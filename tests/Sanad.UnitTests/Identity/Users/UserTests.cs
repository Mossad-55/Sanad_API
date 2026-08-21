using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Identity.Domain.Authentication;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserTests
{
    [Fact]
    public void Create_ShouldStartWithoutPassword()
    {
        User user = CreateUser();

        Assert.False(user.HasPassword);
        Assert.Null(user.Password);
    }

    [Fact]
    public void SetInitialPasswordHash_ShouldStoreCredential()
    {
        User user = CreateUser();

        DateTime utcNow =
            CreateUtcDateTime();

        user.SetInitialPasswordHash(
            "  initial-password-hash  ",
            utcNow);

        Assert.True(user.HasPassword);

        Assert.Equal(
            "initial-password-hash",
            user.Password!.PasswordHash);

        Assert.Equal(
            utcNow,
            user.UpdatedOnUtc);

        Assert.Empty(
            user.DomainEvents
                .OfType<
                    UserPasswordChangedDomainEvent>());
    }

    [Fact]
    public void SetInitialPasswordHash_ShouldRejectExistingPassword()
    {
        User user = CreateUser();

        user.SetInitialPasswordHash(
            "first-hash",
            CreateUtcDateTime());

        PasswordCredential originalPassword =
            user.Password!;

        DateTime originalUpdatedOnUtc =
            user.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => user.SetInitialPasswordHash(
                "second-hash",
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Same(
            originalPassword,
            user.Password);

        Assert.Equal(
            originalUpdatedOnUtc,
            user.UpdatedOnUtc);
    }

    [Fact]
    public void ChangePasswordHash_ShouldReplaceCredentialAndRaiseEvent()
    {
        User user = CreateUser();

        user.SetInitialPasswordHash(
            "initial-hash",
            CreateUtcDateTime());

        user.ClearDomainEvents();

        DateTime changedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        user.ChangePasswordHash(
            "changed-hash",
            changedOnUtc);

        Assert.Equal(
            "changed-hash",
            user.Password!.PasswordHash);

        Assert.Equal(
            changedOnUtc,
            user.UpdatedOnUtc);

        UserPasswordChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserPasswordChangedDomainEvent>());

        Assert.Equal(user.Id, domainEvent.UserId);

        Assert.Equal(
            PasswordChangeReason.Changed,
            domainEvent.Reason);
    }

    [Fact]
    public void ChangePasswordHash_ShouldRejectUserWithoutPassword()
    {
        User user = CreateUser();

        Assert.Throws<DomainException>(
            () => user.ChangePasswordHash(
                "changed-hash",
                CreateUtcDateTime()));

        Assert.False(user.HasPassword);

        Assert.Empty(
            user.DomainEvents
                .OfType<
                    UserPasswordChangedDomainEvent>());
    }

    [Fact]
    public void ResetPasswordHash_ShouldSetCredentialAndRaiseResetEvent()
    {
        User user = CreateUser();

        user.ClearDomainEvents();

        DateTime resetOnUtc =
            CreateUtcDateTime();

        user.ResetPasswordHash(
            "reset-password-hash",
            resetOnUtc);

        Assert.True(user.HasPassword);

        Assert.Equal(
            "reset-password-hash",
            user.Password!.PasswordHash);

        UserPasswordChangedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserPasswordChangedDomainEvent>());

        Assert.Equal(
            PasswordChangeReason.Reset,
            domainEvent.Reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void PasswordOperations_ShouldRejectMissingHash(
        string? passwordHash)
    {
        User user = CreateUser();

        Assert.Throws<DomainException>(
            () => user.SetInitialPasswordHash(
                passwordHash!,
                CreateUtcDateTime()));

        Assert.False(user.HasPassword);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void SetInitialPasswordHash_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        User user = CreateUser();

        DateTime invalidTime =
            DateTime.SpecifyKind(
                CreateUtcDateTime(),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => user.SetInitialPasswordHash(
                "password-hash",
                invalidTime));

        Assert.False(user.HasPassword);
    }

    [Fact]
    public void Create_ShouldGenerateNonEmptyUserId()
    {
        User user = CreateUser();

        UserId userId = user.Id;

        Assert.NotEqual(UserId.Empty, userId);
    }

    [Fact]
    public void Create_ShouldRaiseUserRegisteredDomainEvent()
    {
        User user = CreateUser();

        UserRegisteredDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<UserRegisteredDomainEvent>());

        Assert.Equal(user.Id, domainEvent.UserId);
    }

    [Fact]
    public void Create_ShouldInitializeUserInPendingVerificationStatus()
    {
        User user = CreateUser();

        Assert.Equal(
            UserStatus.PendingVerification,
            user.Status);

        Assert.False(user.EmailVerified);
        Assert.False(user.PhoneVerified);
        Assert.Null(user.LastLoginOnUtc);
        Assert.Empty(user.Accounts);
        Assert.Equal(
            user.CreatedOnUtc,
            user.UpdatedOnUtc);
    }

    [Fact]
    public void Create_ShouldStoreUserInformation()
    {
        FullName arabicFullName =
            FullName.Create("محمد أحمد");

        FullName englishFullName =
            FullName.Create("Mohamed Ahmed");

        Email email =
            Email.Create("mohamed@example.com");

        PhoneNumber phoneNumber =
            PhoneNumber.Create("+201001234567");

        User user = User.Create(
            arabicFullName,
            englishFullName,
            email,
            phoneNumber,
            avatarUrl: "users/avatar.jpg");

        Assert.Equal(
            arabicFullName,
            user.ArabicFullName);

        Assert.Equal(
            englishFullName,
            user.EnglishFullName);

        Assert.Equal(email, user.Email);
        Assert.Equal(phoneNumber, user.PhoneNumber);

        Assert.Equal(
            "users/avatar.jpg",
            user.AvatarUrl);
    }

    [Fact]
    public void Create_ShouldGenerateDifferentIdsForDifferentUsers()
    {
        User firstUser = CreateUser();
        User secondUser = CreateUser();

        Assert.NotEqual(
            firstUser.Id,
            secondUser.Id);
    }

    [Fact]
    public void Create_ShouldStartWithIncompletePersonalInformation()
    {
        User user = CreateUser();

        Assert.Null(user.DateOfBirth);
        Assert.Null(user.Gender);
    }

    [Fact]
    public void CompletePersonalInformation_ShouldStoreDateOfBirthAndGender()
    {
        User user = CreateUser();

        DateOnly currentDate =
            CreateCurrentDate();

        DateOnly dateOfBirth =
            new(1995, 6, 15);

        user.CompletePersonalInformation(
            dateOfBirth,
            Gender.Male,
            currentDate);

        Assert.Equal(
            dateOfBirth,
            user.DateOfBirth);

        Assert.Equal(
            Gender.Male,
            user.Gender);

        Assert.True(
            user.UpdatedOnUtc >=
            user.CreatedOnUtc);
    }

    [Fact]
    public void CompletePersonalInformation_ShouldRejectSecondCompletion()
    {
        User user = CreateUser();

        DateOnly currentDate =
            CreateCurrentDate();

        DateOnly originalDateOfBirth =
            new(1995, 6, 15);

        user.CompletePersonalInformation(
            originalDateOfBirth,
            Gender.Male,
            currentDate);

        DateTime updatedOnUtcAfterCompletion =
            user.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => user.CompletePersonalInformation(
                new DateOnly(1996, 7, 16),
                Gender.Female,
                currentDate));

        Assert.Equal(
            originalDateOfBirth,
            user.DateOfBirth);

        Assert.Equal(
            Gender.Male,
            user.Gender);

        Assert.Equal(
            updatedOnUtcAfterCompletion,
            user.UpdatedOnUtc);
    }

    [Fact]
    public void CompletePersonalInformation_ShouldRejectFutureDateOfBirth()
    {
        User user = CreateUser();

        DateOnly currentDate =
            CreateCurrentDate();

        Assert.Throws<DomainException>(
            () => user.CompletePersonalInformation(
                currentDate.AddDays(1),
                Gender.Male,
                currentDate));

        Assert.Null(user.DateOfBirth);
        Assert.Null(user.Gender);
    }

    [Fact]
    public void CompletePersonalInformation_ShouldRejectInvalidGender()
    {
        User user = CreateUser();

        Assert.Throws<DomainException>(
            () => user.CompletePersonalInformation(
                new DateOnly(1995, 6, 15),
                (Gender)999,
                CreateCurrentDate()));

        Assert.Null(user.DateOfBirth);
        Assert.Null(user.Gender);
    }

    [Fact]
    public void ChangeGender_ShouldRejectIncompletePersonalInformation()
    {
        User user = CreateUser();

        Assert.Throws<DomainException>(
            () => user.ChangeGender(
                Gender.Female));

        Assert.Null(user.Gender);
    }

    [Fact]
    public void ChangeGender_ShouldUpdateCompletedPersonalInformation()
    {
        User user = CreateUser();

        user.CompletePersonalInformation(
            new DateOnly(1995, 6, 15),
            Gender.Male,
            CreateCurrentDate());

        user.ChangeGender(
            Gender.Female);

        Assert.Equal(
            Gender.Female,
            user.Gender);

        Assert.Equal(
            new DateOnly(1995, 6, 15),
            user.DateOfBirth);
    }

    [Fact]
    public void ChangeGender_ShouldRejectInvalidValueWithoutMutation()
    {
        User user = CreateUser();

        user.CompletePersonalInformation(
            new DateOnly(1995, 6, 15),
            Gender.Male,
            CreateCurrentDate());

        DateTime originalUpdatedOnUtc =
            user.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => user.ChangeGender(
                (Gender)999));

        Assert.Equal(
            Gender.Male,
            user.Gender);

        Assert.Equal(
            originalUpdatedOnUtc,
            user.UpdatedOnUtc);
    }

    [Fact]
    public void CorrectDateOfBirth_ShouldRejectIncompletePersonalInformation()
    {
        User user = CreateUser();

        Assert.Throws<DomainException>(
            () => user.CorrectDateOfBirth(
                new DateOnly(1995, 6, 15),
                CreateCurrentDate()));

        Assert.Null(user.DateOfBirth);
    }

    [Fact]
    public void CorrectDateOfBirth_ShouldUpdateDateOfBirth()
    {
        User user = CreateUser();

        user.CompletePersonalInformation(
            new DateOnly(1995, 6, 15),
            Gender.Male,
            CreateCurrentDate());

        DateOnly correctedDate =
            new(1994, 5, 10);

        user.CorrectDateOfBirth(
            correctedDate,
            CreateCurrentDate());

        Assert.Equal(
            correctedDate,
            user.DateOfBirth);

        Assert.Equal(
            Gender.Male,
            user.Gender);
    }

    [Fact]
    public void CorrectDateOfBirth_ShouldRejectFutureDateWithoutMutation()
    {
        User user = CreateUser();

        DateOnly originalDateOfBirth =
            new(1995, 6, 15);

        user.CompletePersonalInformation(
            originalDateOfBirth,
            Gender.Male,
            CreateCurrentDate());

        DateTime originalUpdatedOnUtc =
            user.UpdatedOnUtc;

        DateOnly currentDate =
            CreateCurrentDate();

        Assert.Throws<DomainException>(
            () => user.CorrectDateOfBirth(
                currentDate.AddDays(1),
                currentDate));

        Assert.Equal(
            originalDateOfBirth,
            user.DateOfBirth);

        Assert.Equal(
            originalUpdatedOnUtc,
            user.UpdatedOnUtc);
    }

    [Fact]
    public void RehashPasswordHash_ShouldReplaceHashWithoutChangeEvent()
    {
        User user = CreateUser();

        user.SetInitialPasswordHash(
            "old-hash",
            CreateUtcDateTime());

        user.ClearDomainEvents();

        DateTime rehashedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(1);

        user.RehashPasswordHash(
            "new-rehashed-value",
            rehashedOnUtc);

        Assert.Equal(
            "new-rehashed-value",
            user.Password!.PasswordHash);

        Assert.Equal(
            rehashedOnUtc,
            user.UpdatedOnUtc);

        Assert.Empty(
            user.DomainEvents
                .OfType<
                    UserPasswordChangedDomainEvent>());
    }

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("mohamed@example.com"),
            PhoneNumber.Create("+201001234567"));
    }

    private static DateOnly CreateCurrentDate()
    {
        return new DateOnly(
            2026,
            8,
            20);
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