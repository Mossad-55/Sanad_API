using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;

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

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("mohamed@example.com"),
            PhoneNumber.Create("+201001234567"));
    }
}