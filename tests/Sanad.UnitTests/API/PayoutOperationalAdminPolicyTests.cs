using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.API;
using Sanad.API.Authorization;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

public sealed class PayoutOperationalAdminPolicyTests
{
    [Fact]
    public async Task PayoutOperationalAdmin_AllowsSuperAdminAndFinanceAdminOnly()
    {
        IServiceCollection services =
            new ServiceCollection();

        services.AddSanadApi(
            CreateConfiguration());

        using ServiceProvider provider =
            services.BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateScopes = true
                });

        IAuthorizationPolicyProvider policyProvider =
            provider.GetRequiredService<
                IAuthorizationPolicyProvider>();

        AuthorizationPolicy? policy =
            await policyProvider.GetPolicyAsync(
                AuthorizationPolicies.PayoutOperationalAdmin);

        Assert.NotNull(policy);
        Assert.Contains(
            policy.Requirements,
            requirement =>
                requirement is DenyAnonymousAuthorizationRequirement);

        ClaimsAuthorizationRequirement access =
            Assert.Single(
                policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
                requirement =>
                    requirement.ClaimType == AuthClaimNames.AccessType);

        Assert.Equal(
            [AuthAccessType.Normal.ToString()],
            access.AllowedValues);

        ClaimsAuthorizationRequirement account =
            Assert.Single(
                policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
                requirement =>
                    requirement.ClaimType == AuthClaimNames.AccountType);

        IEnumerable<string>? allowedValues =
            account.AllowedValues;

        Assert.NotNull(allowedValues);

        Assert.Equal(
            [
                AccountType.SuperAdmin.ToString(),
                AccountType.FinanceAdmin.ToString()
            ],
            allowedValues!);

        Assert.DoesNotContain(
            AccountType.SupportAdmin.ToString(),
            allowedValues!);

        Assert.DoesNotContain(
            AccountType.ContentAdmin.ToString(),
            allowedValues!);
    }

    private static IConfiguration CreateConfiguration()
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:IdentityDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test"),

                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Issuer",
                    "Sanad.Api"),

                new KeyValuePair<string, string?>(
                    "Identity:Jwt:Audience",
                    "Sanad.Clients"),

                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey",
                    "12345678901234567890123456789012")
            ])
            .Build();
    }
}
