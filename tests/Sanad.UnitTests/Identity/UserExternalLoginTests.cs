using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Authentication.ExternalLogins;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Identity.Domain.Users.Events;

namespace Sanad.UnitTests.Identity;

public sealed class UserExternalLoginTests
{
    [Fact]
    public void Create_ShouldStoreNormalizedExternalLogin()
    {
        DateTime utcNow =
            CreateUtcDateTime();

        UserExternalLogin externalLogin =
            UserExternalLogin.Create(
                ExternalLoginProvider.Google,
                "  google-subject-123  ",
                utcNow);

        Assert.NotEqual(
            UserExternalLoginId.Empty,
            externalLogin.Id);

        Assert.Equal(
            ExternalLoginProvider.Google,
            externalLogin.Provider);

        Assert.Equal(
            "google-subject-123",
            externalLogin.ProviderSubject);

        Assert.Equal(
            utcNow,
            externalLogin.LinkedOnUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectMissingProviderSubject(
        string? providerSubject)
    {
        Assert.Throws<DomainException>(
            () => UserExternalLogin.Create(
                ExternalLoginProvider.Google,
                providerSubject!,
                CreateUtcDateTime()));
    }

    [Fact]
    public void Create_ShouldRejectInvalidProvider()
    {
        Assert.Throws<DomainException>(
            () => UserExternalLogin.Create(
                (ExternalLoginProvider)999,
                "provider-subject",
                CreateUtcDateTime()));
    }

    [Fact]
    public void Create_ShouldRejectLongProviderSubject()
    {
        string longSubject = new(
            'A',
            UserExternalLogin
                .MaximumProviderSubjectLength + 1);

        Assert.Throws<DomainException>(
            () => UserExternalLogin.Create(
                ExternalLoginProvider.Google,
                longSubject,
                CreateUtcDateTime()));
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Create_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        DateTime invalidTime =
            DateTime.SpecifyKind(
                CreateUtcDateTime(),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => UserExternalLogin.Create(
                ExternalLoginProvider.Google,
                "provider-subject",
                invalidTime));
    }

    [Fact]
    public void LinkExternalLogin_ShouldAddLoginAndRaiseEvent()
    {
        User user = CreateUser();

        user.ClearDomainEvents();

        DateTime linkedOnUtc =
            CreateUtcDateTime();

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "google-subject-123",
            linkedOnUtc);

        UserExternalLogin externalLogin =
            Assert.Single(
                user.ExternalLogins);

        Assert.Equal(
            ExternalLoginProvider.Google,
            externalLogin.Provider);

        Assert.True(user.HasExternalLogin);

        Assert.Equal(
            linkedOnUtc,
            user.UpdatedOnUtc);

        UserExternalLoginLinkedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserExternalLoginLinkedDomainEvent>());

        Assert.Equal(
            user.Id,
            domainEvent.UserId);

        Assert.Equal(
            externalLogin.Id,
            domainEvent.ExternalLoginId);

        Assert.Equal(
            ExternalLoginProvider.Google,
            domainEvent.Provider);
    }

    [Fact]
    public void LinkExternalLogin_ShouldAllowGoogleAndApple()
    {
        User user = CreateUser();

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "google-subject",
            CreateUtcDateTime());

        user.LinkExternalLogin(
            ExternalLoginProvider.Apple,
            "apple-subject",
            CreateUtcDateTime()
                .AddMinutes(1));

        Assert.Equal(
            2,
            user.ExternalLogins.Count);
    }

    [Fact]
    public void LinkExternalLogin_ShouldRejectDuplicateProvider()
    {
        User user = CreateUser();

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "first-google-subject",
            CreateUtcDateTime());

        DateTime originalUpdatedOnUtc =
            user.UpdatedOnUtc;

        user.ClearDomainEvents();

        Assert.Throws<DomainException>(
            () => user.LinkExternalLogin(
                ExternalLoginProvider.Google,
                "second-google-subject",
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Single(user.ExternalLogins);

        Assert.Equal(
            originalUpdatedOnUtc,
            user.UpdatedOnUtc);

        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void UnlinkExternalLogin_ShouldRejectFinalAuthenticationMethod()
    {
        User user = CreateUser();

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "google-subject",
            CreateUtcDateTime());

        user.ClearDomainEvents();

        Assert.Throws<DomainException>(
            () => user.UnlinkExternalLogin(
                ExternalLoginProvider.Google,
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Single(user.ExternalLogins);
        Assert.True(user.HasExternalLogin);
        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void UnlinkExternalLogin_ShouldAllowPasswordUser()
    {
        User user = CreateUser();

        user.SetInitialPasswordHash(
            "password-hash",
            CreateUtcDateTime());

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "google-subject",
            CreateUtcDateTime()
                .AddMinutes(1));

        user.ClearDomainEvents();

        user.UnlinkExternalLogin(
            ExternalLoginProvider.Google,
            CreateUtcDateTime()
                .AddMinutes(2));

        Assert.Empty(user.ExternalLogins);
        Assert.False(user.HasExternalLogin);

        UserExternalLoginUnlinkedDomainEvent domainEvent =
            Assert.Single(
                user.DomainEvents
                    .OfType<
                        UserExternalLoginUnlinkedDomainEvent>());

        Assert.Equal(
            user.Id,
            domainEvent.UserId);

        Assert.Equal(
            ExternalLoginProvider.Google,
            domainEvent.Provider);
    }

    [Fact]
    public void UnlinkExternalLogin_ShouldAllowWhenAnotherProviderRemains()
    {
        User user = CreateUser();

        user.LinkExternalLogin(
            ExternalLoginProvider.Google,
            "google-subject",
            CreateUtcDateTime());

        user.LinkExternalLogin(
            ExternalLoginProvider.Apple,
            "apple-subject",
            CreateUtcDateTime()
                .AddMinutes(1));

        user.UnlinkExternalLogin(
            ExternalLoginProvider.Google,
            CreateUtcDateTime()
                .AddMinutes(2));

        UserExternalLogin remaining =
            Assert.Single(
                user.ExternalLogins);

        Assert.Equal(
            ExternalLoginProvider.Apple,
            remaining.Provider);
    }

    [Fact]
    public void UnlinkExternalLogin_ShouldRejectMissingProvider()
    {
        User user = CreateUser();

        DateTime originalUpdatedOnUtc =
            user.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => user.UnlinkExternalLogin(
                ExternalLoginProvider.Google,
                CreateUtcDateTime()));

        Assert.Equal(
            originalUpdatedOnUtc,
            user.UpdatedOnUtc);
    }

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void LinkExternalLogin_ShouldRejectNonUtcTime(
        DateTimeKind dateTimeKind)
    {
        User user = CreateUser();

        DateTime invalidTime =
            DateTime.SpecifyKind(
                CreateUtcDateTime(),
                dateTimeKind);

        Assert.Throws<DomainException>(
            () => user.LinkExternalLogin(
                ExternalLoginProvider.Google,
                "google-subject",
                invalidTime));

        Assert.Empty(user.ExternalLogins);
    }

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            Email.Create("mohamed@example.com"),
            PhoneNumber.Create("+201001234567"));
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