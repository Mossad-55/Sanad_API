using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Users;

public sealed class AdminAccountInvariantTests
{
    [Theory]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.ContentAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    public void AddAccount_ShouldAllowSingleAdministrativeAccount(
        AccountType accountType)
    {
        User user =
            CreateUser();

        user.AddAccount(
            accountType);

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            accountType,
            account.AccountType);
    }

    [Theory]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    [InlineData(AccountType.Elderly)]
    public void AddAccount_ShouldRejectAppAccount_WhenUserHasAdministrativeAccount(
        AccountType appAccountType)
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.SuperAdmin);

        Assert.Throws<DomainException>(
            () => user.AddAccount(
                appAccountType));

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            AccountType.SuperAdmin,
            account.AccountType);
    }

    [Theory]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    [InlineData(AccountType.Elderly)]
    public void AddAccount_ShouldRejectAdministrativeAccount_WhenUserHasAppAccount(
        AccountType appAccountType)
    {
        User user =
            CreateUser();

        user.AddAccount(
            appAccountType);

        Assert.Throws<DomainException>(
            () => user.AddAccount(
                AccountType.SuperAdmin));

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            appAccountType,
            account.AccountType);
    }

    [Fact]
    public void AddAccount_ShouldRejectSecondAdministrativeAccount()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.SuperAdmin);

        Assert.Throws<DomainException>(
            () => user.AddAccount(
                AccountType.ContentAdmin));

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            AccountType.SuperAdmin,
            account.AccountType);
    }

    [Fact]
    public void AddAccount_ShouldRejectMixingContentAdminWithSupportAdmin()
    {
        User user =
            CreateUser();

        user.AddAccount(
            AccountType.ContentAdmin);

        Assert.Throws<DomainException>(
            () => user.AddAccount(
                AccountType.SupportAdmin));

        UserAccount account =
            Assert.Single(user.Accounts);

        Assert.Equal(
            AccountType.ContentAdmin,
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