using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

public sealed class AdminBankLookupAuthorizationTests
{
    [Fact]
    public void AdminLookupsController_UsesCaregiversAdminPolicy()
    {
        AuthorizeAttribute authorize =
            Assert.Single(
                typeof(AdminLookupsController)
                    .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            AuthorizationPolicies.CaregiversAdmin,
            authorize.Policy);
    }
}
