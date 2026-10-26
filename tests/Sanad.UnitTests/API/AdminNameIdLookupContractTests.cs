using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.Modules.Caregivers.Application.Discovery;
using Sanad.Modules.Caregivers.Application.Lookups;
using Sanad.Modules.Families.Application.Families;

namespace Sanad.UnitTests.API;

/// <summary>
/// Contract tests for the two dedicated Admin name+id lookups: the authorization
/// policy of each route (SuperAdmin+SupportAdmin through existing operational
/// policies, no new grant and no FinanceAdmin), the exact route templates, and
/// the published payload shape. The payload checks are the privacy lock: a
/// lookup response may only ever expose the id and the two bilingual names, and
/// no contact, address, medical or note field may appear on either item type.
/// </summary>
public sealed class AdminNameIdLookupContractTests
{
    [Fact]
    public void Elderly_lookup_uses_the_existing_operational_read_policy()
    {
        AuthorizeAttribute authorize = Assert.Single(
            typeof(AdminElderlyLookupController)
                .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.OperationalReadAdmin, authorize.Policy);
        Assert.Equal(
            "api/v1/admin/elderly",
            typeof(AdminElderlyLookupController)
                .GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("lookup", GetHttpGetTemplate<AdminElderlyLookupController>("Lookup"));
    }

    [Fact]
    public void Caregiver_lookup_uses_the_existing_caregiver_review_policy()
    {
        AuthorizeAttribute authorize = Assert.Single(
            typeof(AdminCaregiverLookupController)
                .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.CaregiverReviewAdmin, authorize.Policy);
        Assert.Equal(
            "api/v1/admin/caregivers",
            typeof(AdminCaregiverLookupController)
                .GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal("lookup", GetHttpGetTemplate<AdminCaregiverLookupController>("Lookup"));
    }

    [Fact]
    public void Caregiver_lookup_literal_segment_does_not_collide_with_the_caregiver_detail_route()
    {
        string[] existingTemplates = [.. typeof(AdminCaregiversController)
            .GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpGetAttribute>())
            .Select(attribute => attribute.Template ?? string.Empty)];

        // The literal lookup segment and the dynamic detail segment are
        // different templates, so both routes register and the literal one wins
        // for "lookup".
        Assert.DoesNotContain("lookup", existingTemplates);
        Assert.Contains("{caregiverId:guid}", existingTemplates);
    }

    [Fact]
    public void Both_lookup_items_publish_the_id_and_the_two_names_only()
    {
        AssertItemShape(typeof(AdminElderlyLookupItem));
        AssertItemShape(typeof(AdminCaregiverLookupItem));
    }

    [Fact]
    public void Both_lookup_pages_publish_the_same_five_field_envelope()
    {
        string[] elderlyEnvelope = [.. typeof(AdminElderlyLookupPage)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];
        string[] caregiverEnvelope = [.. typeof(PagedResult<>)
            .GetProperties()
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)];

        Assert.Equal(caregiverEnvelope, elderlyEnvelope);
        Assert.Equal(
            ["Items", "Page", "PageSize", "TotalCount", "TotalPages"],
            caregiverEnvelope);
    }

    private static void AssertItemShape(Type item)
    {
        PropertyInfo[] properties = [.. item.GetProperties()
            .OrderBy(property => property.Name, StringComparer.Ordinal)];

        Assert.Equal(
            ["ArabicFullName", "EnglishFullName", "Id"],
            properties.Select(property => property.Name));
        Assert.Equal(typeof(string), properties[0].PropertyType);
        Assert.Equal(typeof(string), properties[1].PropertyType);
        Assert.Equal(typeof(Guid), properties[2].PropertyType);

        Assert.All(properties, property =>
        {
            foreach (string forbidden in new[]
                     {
                         "phone", "email", "address", "medical", "health",
                         "note", "emergency", "national", "iban", "bank"
                     })
            {
                Assert.DoesNotContain(
                    forbidden,
                    property.Name,
                    StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    private static string? GetHttpGetTemplate<TController>(string actionName) =>
        typeof(TController)
            .GetMethod(actionName)!
            .GetCustomAttribute<HttpGetAttribute>()!
            .Template;
}
