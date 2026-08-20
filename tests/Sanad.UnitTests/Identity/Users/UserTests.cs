using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserTests
{
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
}