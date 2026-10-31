using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.API.Authorization;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// The real role matrix of the two Admin name+id lookup policies, evaluated
/// through the production <see cref="IAuthorizationPolicyProvider"/> and
/// <see cref="IAuthorizationService"/> of the isolated <see cref="PortalApiMetadataHost"/>
/// (not a hand-built copy of the requirements). This drives the app's actual
/// registered policies — including the new lookup-only
/// <see cref="AuthorizationPolicies.CaregiverLookupAdmin"/> — against
/// principals that carry exactly the two access claims the policies require.
/// <para>
/// Proven here: the caregiver lookup admits only Normal SuperAdmin and
/// FinanceAdmin (SupportAdmin's prior access is explicitly removed,
/// ContentAdmin and app audiences stay denied); the elderly lookup stays
/// SuperAdmin + SupportAdmin with ContentAdmin and FinanceAdmin denied; and
/// the shared caregiver review policy is untouched by the lookup change.
/// </para>
/// </summary>
public sealed class AdminNameIdLookupRoleMatrixTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public AdminNameIdLookupRoleMatrixTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task CaregiverLookupPolicy_AdmitsOnlyNormalSuperAdminAndFinanceAdmin()
    {
        // Allowed — the agreed screen-to-role matrix.
        await AssertAdmittedAsync(
            AuthorizationPolicies.CaregiverLookupAdmin,
            allowed: [(AccountType.SuperAdmin, AuthAccessType.Normal), (AccountType.FinanceAdmin, AuthAccessType.Normal)]);

        // Denied — SupportAdmin's prior lookup access is explicitly removed;
        // ContentAdmin and app audiences never had it.
        await AssertDeniedAsync(
            AuthorizationPolicies.CaregiverLookupAdmin,
            denied:
            [
                (AccountType.SupportAdmin, AuthAccessType.Normal),
                (AccountType.ContentAdmin, AuthAccessType.Normal),
                (AccountType.Family, AuthAccessType.Normal),
                (AccountType.SuperAdmin, AuthAccessType.RestrictedVerification),
                (AccountType.FinanceAdmin, AuthAccessType.RestrictedVerification),
            ]);
    }

    [Fact]
    public async Task ElderlyLookupPolicy_KeepsNormalSuperAdminAndSupportAdminOnly()
    {
        await AssertAdmittedAsync(
            AuthorizationPolicies.OperationalReadAdmin,
            allowed: [(AccountType.SuperAdmin, AuthAccessType.Normal), (AccountType.SupportAdmin, AuthAccessType.Normal)]);

        await AssertDeniedAsync(
            AuthorizationPolicies.OperationalReadAdmin,
            denied:
            [
                (AccountType.ContentAdmin, AuthAccessType.Normal),
                (AccountType.FinanceAdmin, AuthAccessType.Normal),
                (AccountType.Family, AuthAccessType.Normal),
                (AccountType.SuperAdmin, AuthAccessType.RestrictedVerification),
            ]);
    }

    [Fact]
    public async Task CaregiverReviewAdminPolicy_IsUnchangedByTheLookupPolicy()
    {
        // The review/ratings surfaces keep SuperAdmin + SupportAdmin and
        // deliberately do not gain FinanceAdmin: the lookup change is scoped
        // to the lookup route only.
        await AssertAdmittedAsync(
            AuthorizationPolicies.CaregiverReviewAdmin,
            allowed: [(AccountType.SuperAdmin, AuthAccessType.Normal), (AccountType.SupportAdmin, AuthAccessType.Normal)]);

        await AssertDeniedAsync(
            AuthorizationPolicies.CaregiverReviewAdmin,
            denied:
            [
                (AccountType.FinanceAdmin, AuthAccessType.Normal),
                (AccountType.ContentAdmin, AuthAccessType.Normal),
                (AccountType.Family, AuthAccessType.Normal),
            ]);
    }

    // ================================ harness ====================================

    /// <summary>Evaluates the app's actually registered policy for each role.</summary>
    private async Task AssertAdmittedAsync(
        string policyName,
        (AccountType Account, AuthAccessType Access)[] allowed)
    {
        AuthorizationPolicy policy = await PolicyAsync(policyName);

        foreach ((AccountType account, AuthAccessType access) in allowed)
        {
            AuthorizationResult result =
                await AuthorizeAsync(policy, Principal(account, access));

            Assert.True(result.Succeeded, $"{policyName} should admit {account}/{access}.");
        }
    }

    private async Task AssertDeniedAsync(
        string policyName,
        (AccountType Account, AuthAccessType Access)[] denied)
    {
        AuthorizationPolicy policy = await PolicyAsync(policyName);

        foreach ((AccountType account, AuthAccessType access) in denied)
        {
            AuthorizationResult result =
                await AuthorizeAsync(policy, Principal(account, access));

            Assert.False(result.Succeeded, $"{policyName} should deny {account}/{access}.");
        }
    }

    private async Task<AuthorizationPolicy> PolicyAsync(string policyName)
    {
        IAuthorizationPolicyProvider provider =
            _host.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        AuthorizationPolicy policy = await provider.GetPolicyAsync(policyName)
            ?? throw new InvalidOperationException(
                $"Policy '{policyName}' is not registered in the app's options.");

        return policy;
    }

    private Task<AuthorizationResult> AuthorizeAsync(
        AuthorizationPolicy policy,
        ClaimsPrincipal principal) =>
        _host.Services.GetRequiredService<IAuthorizationService>()
            .AuthorizeAsync(principal, resource: null, policy: policy);

    private static ClaimsPrincipal Principal(
        AccountType accountType,
        AuthAccessType accessType) => new(new ClaimsIdentity(
        [
            new Claim(AuthClaimNames.AccessType, accessType.ToString()),
            new Claim(AuthClaimNames.AccountType, accountType.ToString()),
        ], authenticationType: "Test"));
}
