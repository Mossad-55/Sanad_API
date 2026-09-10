using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Users;

public sealed class UserChangeNameTests
{
    [Fact]
    public void ChangeName_ShouldUpdateBothNamesAndRaiseNoContactEvent()
    {
        User user =
            CreateUser();

        user.ClearDomainEvents();

        DateTime changedOnUtc =
            CreateUtcDateTime()
                .AddMinutes(2);

        user.ChangeName(
            FullName.Create(
                "محمد جديد"),
            FullName.Create(
                "Mohamed New"),
            changedOnUtc);

        Assert.Equal(
            "محمد جديد",
            user.ArabicFullName.Value);

        Assert.Equal(
            "Mohamed New",
            user.EnglishFullName.Value);

        Assert.Equal(
            changedOnUtc,
            user.UpdatedOnUtc);

        Assert.Empty(user.DomainEvents);
    }

    [Fact]
    public void ChangeName_ShouldRejectBlockedUser()
    {
        User user =
            CreateUser();

        user.Block(
            "Security block.",
            CreateUtcDateTime());

        user.ClearDomainEvents();

        Assert.Throws<DomainException>(
            () => user.ChangeName(
                FullName.Create(
                    "محمد جديد"),
                FullName.Create(
                    "Mohamed New"),
                CreateUtcDateTime()
                    .AddMinutes(1)));

        Assert.Equal(
            "محمد أحمد",
            user.ArabicFullName.Value);

        Assert.Equal(
            "Mohamed Ahmed",
            user.EnglishFullName.Value);
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
