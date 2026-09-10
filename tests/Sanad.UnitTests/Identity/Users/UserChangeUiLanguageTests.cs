using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserChangeUiLanguageTests
{
    [Fact]
    public void ChangeUiLanguage_ShouldSetValueAndUpdatedOnUtc()
    {
        User user =
            CreateUser();

        user.ClearDomainEvents();

        DateTime changedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(2);

        user.ChangeUiLanguage(
            UiLanguage.English,
            changedOnUtc);

        Assert.Equal(
            UiLanguage.English,
            user.UiLanguage);

        Assert.Equal(
            changedOnUtc,
            user.UpdatedOnUtc);

        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void ChangeUiLanguage_ShouldRejectUndefinedValue()
    {
        User user =
            CreateUser();

        Assert.Throws<DomainException>(
            () => user.ChangeUiLanguage(
                (UiLanguage)99,
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            UiLanguage.Arabic,
            user.UiLanguage);
    }

    [Fact]
    public void ChangeUiLanguage_ShouldRejectBlockedUser()
    {
        User user =
            CreateUser();

        user.Block(
            "Security block.",
            CreateUtcDateTime());

        user.ClearDomainEvents();

        Assert.Throws<DomainException>(
            () => user.ChangeUiLanguage(
                UiLanguage.English,
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            UiLanguage.Arabic,
            user.UiLanguage);
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
