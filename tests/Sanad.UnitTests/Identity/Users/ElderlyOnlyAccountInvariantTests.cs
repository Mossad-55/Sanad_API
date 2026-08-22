using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Users;

public sealed class ElderlyOnlyAccountInvariantTests
{
    [Fact]
    public void AddAccount_ShouldRejectElderlyAccount_WhenUserAlreadyHasAnotherAccount()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.Family);

        Assert.Throws<DomainException>(
            () => user.AddAccount(
                AccountType.Elderly));

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            AccountType.Family,
            account.AccountType);
    }

    [Fact]
    public void AddAccount_ShouldRejectNonElderlyAccount_WhenUserAlreadyHasElderlyAccount()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.Elderly);

        Assert.Throws<DomainException>(
            () => user.AddAccount(
                AccountType.Family));

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            AccountType.Elderly,
            account.AccountType);
    }

    private static User CreateUser()
    {
        return User.Create(
            FullName.Create("محمد أحمد"),
            FullName.Create("Mohamed Ahmed"),
            email: null,
            PhoneNumber.Create("+201001234567"));
    }
}
