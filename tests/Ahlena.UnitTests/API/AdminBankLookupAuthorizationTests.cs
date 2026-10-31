using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;

namespace Ahlena.UnitTests.API;

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
