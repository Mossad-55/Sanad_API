using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Identity.Users;

public sealed class CreateElderlyIdentityFactoryTests
{
    [Fact]
    public void CreateElderly_ProducesActivePhoneVerifiedSingleElderlyAccount()
    {
        User user = User.CreateElderly(
            FullName.Create("سعيد"),
            FullName.Create("Saeed"),
            PhoneNumber.Create("+201001234567"),
            Gender.Male,
            new DateOnly(1948, 7, 20),
            DateTime.UtcNow);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.True(user.PhoneVerified);
        Assert.Null(user.Email);
        Assert.False(user.HasPassword);
        Assert.Single(user.Accounts);
        Assert.Equal(
            AccountType.Elderly,
            user.Accounts.Single().AccountType);
    }
}