using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.CareHomes.Application.Abstractions.Data;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Finance.Domain;
using Sanad.Modules.Finance.Infrastructure;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.Sos;
using Sanad.Modules.Families.Domain.Sos;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Application.Users.Admin;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.UnitTests.API;

/// <summary>
/// PORTAL-03 evidence over the real generated OpenAPI document served by the
/// isolated <see cref="PortalApiMetadataHost"/> (production registration and
/// controllers; no handwritten schema). Metadata GETs need no data fixtures.
/// </summary>
public sealed class PortalOpenApiContractTests : IClassFixture<PortalApiMetadataHost>
{
    private static readonly object DocumentSync = new();

    private static JsonDocument? _cachedDocument;

    private readonly PortalApiMetadataHost _host;

    public PortalOpenApiContractTests(PortalApiMetadataHost host) => _host = host;

    public static TheoryData<string, string, int, string, string> SuccessOperations => new()
    {
        { "get", "/api/v1/admin/users", 200, "ref", "AdminUserListResponse" },
        { "get", "/api/v1/admin/users/{userId}", 200, "ref", "AdminUserDetailResponse" },
        { "get", "/api/v1/admin/users/sessions", 200, "ref", "AdminSessionOverviewResponse" },
        { "get", "/api/v1/admin/users/audit", 200, "ref", "AdminUserAuditListResponse" },
        { "get", "/api/v1/admin/elderly/check-ins", 200, "ref", "PagedAdminElderlyCheckIns" },
        { "get", "/api/v1/admin/elderly/check-ins/aggregate", 200, "ref", "AdminElderlyCheckInAggregate" },
        { "get", "/api/v1/admin/elderly/help-requests", 200, "ref", "PagedElderlyHelpRequests" },
        { "get", "/api/v1/admin/elderly/help-requests/{requestId}", 200, "ref", "ElderlyHelpRequestResponse" },
        { "get", "/api/v1/admin/elderly/help-requests/{requestId}/history", 200, "array", "ElderlyHelpRequestHistoryResponse" },
        { "get", "/api/v1/admin/elderly/help-requests/aggregate", 200, "dict", "HelpRequestAggregate" },
        { "get", "/api/v1/admin/elderly/medications", 200, "ref", "PagedAdminMedicationRecords" },
        { "get", "/api/v1/admin/elderly/medications/adherence", 200, "ref", "AdminMedicationAdherenceAggregate" },
        { "get", "/api/v1/admin/elderly/sos", 200, "ref", "PagedElderlySos" },
        { "get", "/api/v1/admin/care-homes/check-in-disputes", 200, "array", "AdminCareHomeCheckInDispute" },
        { "get", "/api/v1/admin/cms/elderly-welcome", 200, "ref", "ElderlyWelcomeResponse" },
        { "get", "/api/v1/admin/finance/platform-charge-rules/current", 200, "ref", "PlatformChargeRuleResponse" },
        { "get", "/api/v1/admin/finance/platform-charge-rules/history", 200, "array", "PlatformChargeRuleResponse" },
        { "post", "/api/v1/admin/finance/platform-charge-rules", 201, "guid", "" },
        { "get", "/api/v1/admin/platform-controls", 200, "ref", "PlatformControlResponse" },
        { "put", "/api/v1/admin/platform-controls", 200, "ref", "PlatformControlResponse" },
        { "get", "/api/v1/platform/status", 200, "ref", "PublicPlatformStatusResponse" },
        { "get", "/api/v1/admin/notifications", 200, "ref", "AdminNotificationPage" },
        { "get", "/api/v1/admin/notifications/{notificationId}", 200, "ref", "AdminNotificationRecord" },
        { "get", "/api/v1/admin/notifications/timeline", 200, "ref", "AdminNotificationPage" },
        { "get", "/api/v1/admin/notifications/aggregate", 200, "ref", "AdminNotificationAggregate" },
        { "get", "/api/v1/notifications/unread-count", 200, "integer", "" },
        { "get", "/api/v1/admin/sentence-builder/catalog", 200, "ref", "SentenceBuilderCatalogPage" },
        { "get", "/api/v1/admin/wellness-tips", 200, "ref", "WellnessTipPageResponse" },
        { "get", "/api/v1/care-homes/inventory/admin/{facilityId}", 200, "ref", "CareHomeInventoryDto" },
        { "get", "/api/v1/care-homes/inventory/mine", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/room-types", 200, "ref", "CareHomeInventoryDto" },
        { "put", "/api/v1/care-homes/inventory/mine/room-types/{id}", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/room-types/{id}/media", 201, "ref", "CareHomeRoomTypeMediaDto" },
        { "delete", "/api/v1/care-homes/inventory/mine/room-types/{id}/media/{mediaId}", 204, "none", "" },
        { "post", "/api/v1/care-homes/inventory/mine/room-types/{id}/archive", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/rooms", 200, "ref", "CareHomeInventoryDto" },
        { "put", "/api/v1/care-homes/inventory/mine/rooms/{id}", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/rooms/{id}/media", 201, "ref", "CareHomeRoomMediaDto" },
        { "delete", "/api/v1/care-homes/inventory/mine/rooms/{id}/media/{mediaId}", 204, "none", "" },
        { "post", "/api/v1/care-homes/inventory/mine/rooms/{id}/archive", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/beds", 200, "ref", "CareHomeInventoryDto" },
        { "put", "/api/v1/care-homes/inventory/mine/beds/{id}", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/beds/{id}/archive", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/maintenance", 200, "ref", "CareHomeInventoryDto" },
        { "put", "/api/v1/care-homes/inventory/mine/maintenance/{id}", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/maintenance/{id}/cancel", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/availability", 200, "array", "CareHomeAvailability" },
        { "post", "/api/v1/care-homes/inventory/admin/{facilityId}/availability", 200, "array", "CareHomeAvailability" },
        { "get", "/api/v1/care-homes/booking-requests/mine", 200, "array", "CareHomeBookingListItem" },
        { "get", "/api/v1/care-homes/visits/mine", 200, "array", "CareHomeVisitResponse" },
        { "get", "/api/v1/care-homes/facilities/mine/visit-settings", 200, "ref", "CareHomeVisitSettingsResponse" },
        { "put", "/api/v1/care-homes/facilities/mine/visit-settings", 200, "ref", "CareHomeVisitSettingsResponse" },
        { "get", "/api/v1/care-homes/dashboard/mine", 200, "ref", "CareHomeDashboard" },
        { "get", "/api/v1/care-homes/revenue/mine", 200, "ref", "CareHomeRevenueReport" },
        { "get", "/api/v1/care-homes/payouts/mine", 200, "array", "CareHomePayoutLedgerItem" },
        { "post", "/api/v1/admin/users", 201, "ref", "RegisterUserResponse" },
        { "post", "/api/v1/admin/users/{userId}/suspend", 200, "ref", "AdminUserStatusResponse" },
        { "post", "/api/v1/admin/users/{userId}/block", 200, "ref", "AdminUserStatusResponse" },
        { "post", "/api/v1/admin/users/{userId}/reactivate", 200, "ref", "AdminUserStatusResponse" },
        { "post", "/api/v1/admin/users/{userId}/password-reset", 200, "ref", "ResetAdminUserPasswordResponse" },
        { "put", "/api/v1/admin/users/{userId}/account-type", 200, "ref", "AdminUserAccountTypeResponse" }
    };

    [Theory]
    [MemberData(nameof(SuccessOperations))]
    public void SuccessOperation_PublishesActualResponseSchema(
        string method, string path, int status, string kind, string schemaName)
    {
        JsonElement root = Document;
        JsonElement operation = GetOperation(root, path, method);
        Assert.True(
            operation.GetProperty("responses").TryGetProperty(status.ToString(), out JsonElement response),
            $"Missing {status} response for {method.ToUpperInvariant()} {path}.");

        if (kind == "none")
        {
            Assert.False(response.TryGetProperty("content", out _), "A 204 response must not publish a body schema.");
            return;
        }

        Assert.True(
            TryGetJsonSchema(response, out JsonElement schema),
            $"Missing application/json schema for {method.ToUpperInvariant()} {path} {status}.");

        if (kind == "array")
        {
            Assert.Equal("array", schema.GetProperty("type").GetString());
            Assert.EndsWith(
                schemaName,
                AssertRef(schema.GetProperty("items"), root));
        }
        else if (kind == "dict")
        {
            Assert.Equal("object", schema.GetProperty("type").GetString());
        }
        else if (kind == "integer")
        {
            JsonElement type = schema.GetProperty("type");
            bool isInteger = type.ValueKind == JsonValueKind.String
                ? type.GetString() == "integer"
                : type.ValueKind == JsonValueKind.Array
                    && type.EnumerateArray().Any(value => value.GetString() == "integer");
            Assert.True(isInteger, "Expected a bare integer response schema.");
        }
        else if (kind == "guid")
        {
            Assert.Equal("string", schema.GetProperty("type").GetString());
            Assert.Equal("uuid", schema.GetProperty("format").GetString());
        }
        else
        {
            Assert.EndsWith(schemaName, AssertRef(schema, root));
        }
    }

    [Fact]
    public void ResendOperation_PublishesProviderDeliveryFailureResponse()
    {
        JsonElement root = Document;
        JsonElement operation = GetOperation(root, "/api/v1/auth/verification/resend", "post");

        Assert.True(
            operation.GetProperty("responses").TryGetProperty("200", out JsonElement ok),
            "Missing 200 response for POST /api/v1/auth/verification/resend.");
        Assert.True(
            TryGetJsonSchema(ok, out JsonElement schema),
            "Missing application/json schema for POST /api/v1/auth/verification/resend 200.");
        Assert.EndsWith("ResendOtpResponse", AssertRef(schema, root));

        Assert.True(
            operation.GetProperty("responses").TryGetProperty("409", out _),
            "Missing 409 response for POST /api/v1/auth/verification/resend.");
        Assert.True(
            operation.GetProperty("responses").TryGetProperty("502", out _),
            "Missing 502 response for POST /api/v1/auth/verification/resend.");
    }

    [Fact]
    public void LoginResponseSchema_IncludesRecoveryFields()
    {
        JsonElement root = Document;
        JsonElement operation = GetOperation(root, "/api/v1/auth/login", "post");

        Assert.True(
            operation.GetProperty("responses").TryGetProperty("200", out JsonElement ok),
            "Missing 200 response for POST /api/v1/auth/login.");
        Assert.True(
            TryGetJsonSchema(ok, out JsonElement schema),
            "Missing application/json schema for POST /api/v1/auth/login 200.");

        string reference = AssertRef(schema, root);
        Assert.EndsWith("LoginResponse", reference);

        JsonElement properties = ResolveRef(root, reference).GetProperty("properties");

        foreach (string property in new[]
        {
            "emailVerificationRequestId",
            "emailVerificationRequestExpiresOnUtc",
            "phoneVerificationRequestId",
            "phoneVerificationRequestExpiresOnUtc"
        })
        {
            Assert.True(
                properties.TryGetProperty(property, out _),
                $"Missing {property} in LoginResponse schema.");
        }
    }

    public static TheoryData<string, string, string> RequiredQueryParameters => new()
    {
        { "get", "/api/v1/admin/care-homes/license-expirations", "through" },
        { "get", "/api/v1/lookups/cities", "governorateId" },
        { "get", "/api/v1/admin/lookups/cities", "governorateId" },
        { "get", "/api/v1/lookups/areas", "cityId" },
        { "get", "/api/v1/admin/lookups/areas", "cityId" }
    };

    [Theory]
    [MemberData(nameof(RequiredQueryParameters))]
    public void RequiredQueryParameter_IsMarkedRequired(string method, string path, string name)
    {
        JsonElement operation = GetOperation(Document, path, method);
        Assert.True(operation.TryGetProperty("parameters", out JsonElement parameters), $"No parameters for {method} {path}.");
        JsonElement? match = parameters.EnumerateArray().FirstOrDefault(
            parameter => parameter.GetProperty("name").GetString() == name &&
                parameter.GetProperty("in").GetString() == "query");
        Assert.True(match is not null, $"Missing query parameter {name} on {method} {path}.");
        Assert.True(match!.Value.GetProperty("required").GetBoolean(), $"Query parameter {name} on {method} {path} is not required.");
    }

    public static TheoryData<string, int[], string[]> NumericEnums => new()
    {
        { "UserStatus", [1, 2, 3, 4], ["PendingVerification", "Active", "Suspended", "Blocked"] },
        { "AccountType", [1, 2, 3, 4, 5, 6, 7, 8, 9], ["Family", "MedicalCaregiver", "CompanionCaregiver", "Elderly", "SuperAdmin", "ContentAdmin", "SupportAdmin", "CareHomeOwner", "FinanceAdmin"] },
        { "DevicePlatform", [0, 1, 2, 3], ["Unknown", "Android", "iOS", "Web"] },
        { "CaregiverType", [1, 2], ["Medical", "Companion"] },
        { "AdminUserActionType", [1, 2, 3, 4, 5, 6, 7, 8], ["Suspended", "Reactivated", "Blocked", "SessionsRevoked", "PasswordReset", "AccountTypeChanged", "Created", "Anonymized"] },
        { "PlatformState", [1, 2], ["Operational", "Maintenance"] },
        { "PlatformPaymentMethod", [1, 2], ["Card", "Wallet"] }
    };

    [Theory]
    [MemberData(nameof(NumericEnums))]
    public void NumericEnumSchema_PublishesValuesAndNames(string name, int[] values, string[] names)
    {
        JsonElement schemas = Document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(name, out JsonElement schema), $"Missing enum schema {name}.");
        string? type = schema.TryGetProperty("type", out JsonElement typeElement) ? typeElement.GetString() : null;
        Assert.True(type is null || type.Contains("integer"), $"Enum schema {name} is not integer-typed (type: {type}).");
        Assert.Equal(values, schema.GetProperty("enum").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.Equal(names, schema.GetProperty("x-enumNames").EnumerateArray().Select(v => v.GetString()).ToArray());
    }

    [Theory]
    [InlineData("UserId")]
    [InlineData("VerificationRequestId")]
    [InlineData("GovernorateId")]
    [InlineData("CityId")]
    [InlineData("AreaId")]
    public void IdWrapperSchema_RequiresNonNullValue(string name)
    {
        JsonElement schemas = Document.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty(name, out JsonElement schema), $"Missing ID schema {name}.");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        JsonElement value = schema.GetProperty("properties").GetProperty("value");
        Assert.Equal("string", value.GetProperty("type").GetString());
        Assert.Equal("uuid", value.GetProperty("format").GetString());
        Assert.Contains("value", schema.GetProperty("required").EnumerateArray().Select(v => v.GetString()));
    }

    /// <summary>
    /// Partner-contact request body. Only the three genuinely required fields
    /// appear in the generated schema's <c>required</c> array; the optional
    /// fields stay out. Wire names/casing are unchanged.
    /// </summary>
    [Fact]
    public void PartnerContactBodySchema_RequiresOnlyNameEmailAndMessage()
    {
        JsonElement operation = GetOperation(Document, "/api/v1/partner-contact", "post");
        JsonElement schema = operation
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        JsonElement body = ResolveRef(Document, AssertRef(schema, Document));

        string[] required = body.TryGetProperty("required", out JsonElement requiredElement)
            ? requiredElement.EnumerateArray().Select(value => value.GetString()!).ToArray()
            : [];

        Assert.Equal(["name", "email", "message"], required);

        JsonElement properties = body.GetProperty("properties");
        foreach (string property in new[] { "name", "email", "message", "company", "subject", "phone", "companyType", "city" })
        {
            Assert.True(properties.TryGetProperty(property, out _), $"Missing {property} in partner-contact schema.");
        }
    }

    [Fact]
    public void CommunityCategoryUpdateBody_DoesNotRequireId()
    {
        JsonElement operation = GetOperation(Document, "/api/v1/admin/community/categories/{categoryId}", "put");
        JsonElement schema = operation
            .GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        string reference = AssertRef(schema, Document);
        JsonElement body = ResolveRef(Document, reference);
        if (body.TryGetProperty("required", out JsonElement required))
        {
            Assert.DoesNotContain(
                required.EnumerateArray().Select(v => v.GetString()),
                value => string.Equals(value, "id", StringComparison.OrdinalIgnoreCase));
        }
        Assert.True(body.GetProperty("properties").TryGetProperty("arabicName", out _));
        Assert.True(body.GetProperty("properties").TryGetProperty("englishName", out _));
        Assert.True(body.GetProperty("properties").TryGetProperty("isActive", out _));
    }

    [Fact]
    public void NullableEnum_AllowsNullAndDefinedValue_RejectsUndefined()
    {
        JsonElement root = Document;
        JsonElement property = ResolveRef(root, "#/components/schemas/MyProfileResponse")
            .GetProperty("properties")
            .GetProperty("accountType");

        // The nullable union wrapper itself must not carry a numeric-only
        // enum constraint: with anyOf/oneOf semantics it would conjunctively
        // reject null. The null branch and the constrained $ref carry it.
        Assert.False(
            property.TryGetProperty("enum", out _),
            "Nullable union wrapper must not define its own enum constraint.");

        List<JsonElement> branches = CollectBranches(root, property);

        Assert.True(branches.Count > 0, "accountType exposes no schema branches.");
        Assert.True(
            branches.Any(AllowsNull),
            "Nullable accountType does not allow null in its complete schema.");
        Assert.True(
            branches.Any(branch => EnumContains(branch, root, 5)),
            "Nullable accountType does not accept the defined SuperAdmin value.");
        Assert.False(
            branches.Any(branch => EnumContains(branch, root, 99)),
            "accountType unexpectedly accepts the undefined value 99.");
        Assert.False(
            branches.SelectMany(EnumValues).Any(v => v.ValueKind == JsonValueKind.Null),
            "Null must not be paired as an enum member.");
    }

    [Fact]
    public void ChargeRuleCurrent_PublishesPopulated200AndBodiless204()
    {
        JsonElement operation = GetOperation(
            Document, "/api/v1/admin/finance/platform-charge-rules/current", "get");
        JsonElement responses = operation.GetProperty("responses");

        Assert.True(
            responses.TryGetProperty("204", out JsonElement noContent),
            "Missing 204 first-run response for the current charge rule.");
        Assert.False(noContent.TryGetProperty("content", out _), "204 must carry no body schema.");
        Assert.True(responses.TryGetProperty("200", out _), "Populated 200 response must be retained.");
    }

    [Fact]
    public void PublicPlatformStatus_PublishesOnlyTheSanitizedShape()
    {
        JsonElement root = Document;
        JsonElement schema = ResolveRef(root, "#/components/schemas/PublicPlatformStatusResponse");

        Assert.Equal(
            ["availablePaymentMethods", "maintenanceMessageArabic", "maintenanceMessageEnglish", "state", "subscriptionCardEnrollmentAvailable"],
            schema.GetProperty("properties")
                .EnumerateObject()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());

        // The public status advertises supported payment methods only; it never
        // carries the control version, the Card/Wallet switches, the internal
        // reason or any actor/audit data.
        AssertRefProperty(root, schema, "availablePaymentMethods", "PlatformPaymentMethod", isArray: true);
    }

    [Fact]
    public void PlatformControlOperations_PublishTheirFailureContracts()
    {
        JsonElement responses = GetOperation(
            Document, "/api/v1/admin/platform-controls", "get").GetProperty("responses");

        Assert.True(
            responses.TryGetProperty("200", out JsonElement ok),
            "Missing 200 response for GET /api/v1/admin/platform-controls.");
        Assert.True(
            TryGetJsonSchema(ok, out JsonElement schema),
            "Missing application/json schema for GET /api/v1/admin/platform-controls 200.");
        Assert.EndsWith("PlatformControlResponse", AssertRef(schema, Document));
        Assert.True(
            responses.TryGetProperty("503", out _),
            "Missing fail-closed 503 response for GET /api/v1/admin/platform-controls.");
        foreach (string status in new[] { "401", "403" })
        {
            Assert.True(
                responses.TryGetProperty(status, out _),
                $"Missing {status} response for GET /api/v1/admin/platform-controls.");
        }

        JsonElement putResponses = GetOperation(
            Document, "/api/v1/admin/platform-controls", "put").GetProperty("responses");
        foreach (string status in new[] { "200", "400", "409", "503", "401", "403" })
        {
            Assert.True(
                putResponses.TryGetProperty(status, out _),
                $"Missing {status} response for PUT /api/v1/admin/platform-controls.");
        }

        JsonElement statusResponses = GetOperation(
            Document, "/api/v1/platform/status", "get").GetProperty("responses");
        Assert.True(
            statusResponses.TryGetProperty("503", out _),
            "Missing fail-closed 503 response for GET /api/v1/platform/status.");
        // The public status route is anonymous and must not advertise auth errors.
        foreach (string status in new[] { "401", "403" })
        {
            Assert.False(
                statusResponses.TryGetProperty(status, out _),
                $"Anonymous GET /api/v1/platform/status must not document {status}.");
        }
    }

    [Fact]
    public void FamilyCheckoutAndPaymentIntentOperations_PublishPlatformControlFailures()
    {
        // E04-B: the gated new checkouts and payment-intent routes document the
        // platform maintenance/method failures they can now return.
        AssertDocumentsResponses("/api/v1/family/bookings/checkout", "post", "503");
        AssertDocumentsResponses("/api/v1/family/bookings/{bookingId}/payments/intent", "post", "400", "503");
        AssertDocumentsResponses("/api/v1/family/care-home-bookings/checkout", "post", "503");
        AssertDocumentsResponses("/api/v1/family/care-home-bookings/{bookingId}/payments/intent", "post", "400", "503");
        AssertDocumentsResponses("/api/v1/family/subscriptions/payment-intent", "post", "400", "503");
        AssertDocumentsResponses("/api/v1/family/subscriptions/renewal/payment-intent", "post", "400", "503");
        AssertDocumentsResponses("/api/v1/family/subscriptions/plan-change/payment-intent", "post", "400", "503");
    }

    private void AssertDocumentsResponses(string path, string method, params string[] statuses)
    {
        JsonElement responses = GetOperation(Document, path, method).GetProperty("responses");

        foreach (string status in statuses)
        {
            Assert.True(
                responses.TryGetProperty(status, out _),
                $"Missing {status} response for {method.ToUpperInvariant()} {path}.");
        }
    }

    [Fact]
    public void NestedResponseRefs_ResolveToActualShapes()
    {
        JsonElement root = Document;
        // Admin user detail: paged list item, sessions with numeric platform, linked facility.
        JsonElement detail = ResolveRef(root, "#/components/schemas/AdminUserDetailResponse");
        AssertRefProperty(root, detail, "sessions", "AdminSessionItem", isArray: true);
        JsonElement session = ResolveRef(root, "#/components/schemas/AdminSessionItem");
        AssertRefProperty(root, session, "platform", "DevicePlatform", isArray: false);
        // Inventory: nested room-type/room/bed/maintenance collections.
        JsonElement inventory = ResolveRef(root, "#/components/schemas/CareHomeInventoryDto");
        AssertRefProperty(root, inventory, "roomTypes", "CareHomeRoomTypeDto", isArray: true);
        AssertRefProperty(root, inventory, "maintenance", "CareHomeMaintenanceDto", isArray: true);
        // Notifications page envelope and record.
        JsonElement page = ResolveRef(root, "#/components/schemas/AdminNotificationPage");
        AssertRefProperty(root, page, "items", "AdminNotificationRecord", isArray: true);
        Assert.True(page.GetProperty("properties").TryGetProperty("hasMore", out _));
        // Password-reset response keeps synthetic-safe shape.
        JsonElement reset = ResolveRef(root, "#/components/schemas/ResetAdminUserPasswordResponse");
        Assert.True(reset.GetProperty("properties").TryGetProperty("temporaryPassword", out _));
        // Nullable properties stay nullable.
        AssertAllowsNull(ResolveRef(root, "#/components/schemas/AdminUserDetailResponse"), "email");
        AssertAllowsNull(ResolveRef(root, "#/components/schemas/ElderlySosResponse"), "latitude");
        AssertAllowsNull(ResolveRef(root, "#/components/schemas/AdminCareHomeCheckInDispute"), "resolvedOnUtc");
    }

    private JsonElement Document
    {
        get
        {
            lock (DocumentSync)
            {
                if (_cachedDocument is null)
                {
                    using HttpClient client = _host.CreatePortalClient();
                    using HttpResponseMessage response = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
                    response.EnsureSuccessStatusCode();
                    _cachedDocument = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                }

                return _cachedDocument.RootElement;
            }
        }
    }

    private static bool TryGetJsonSchema(JsonElement response, out JsonElement schema)
    {
        schema = default;
        return response.TryGetProperty("content", out JsonElement content) &&
            content.TryGetProperty("application/json", out JsonElement media) &&
            media.TryGetProperty("schema", out schema);
    }

    private static JsonElement GetOperation(JsonElement root, string path, string method)
    {
        Assert.True(root.GetProperty("paths").TryGetProperty(path, out JsonElement pathItem), $"Missing path {path}.");
        Assert.True(pathItem.TryGetProperty(method, out JsonElement operation), $"Missing {method} on {path}.");
        return operation;
    }

    private static string AssertRef(JsonElement schema, JsonElement root)
    {
        Assert.True(schema.TryGetProperty("$ref", out JsonElement reference), "Expected a $ref schema.");
        string value = reference.GetString()!;
        ResolveRef(root, value);
        return value;
    }

    private static JsonElement ResolveRef(JsonElement root, string reference)
    {
        const string prefix = "#/components/schemas/";
        Assert.StartsWith(prefix, reference);
        string name = reference[prefix.Length..];
        Assert.True(
            root.GetProperty("components").GetProperty("schemas").TryGetProperty(name, out JsonElement schema),
            $"Unresolvable schema reference {reference}.");
        return schema;
    }

    private static void AssertRefProperty(JsonElement root, JsonElement parent, string property, string target, bool isArray)
    {
        Assert.True(parent.GetProperty("properties").TryGetProperty(property, out JsonElement element), $"Missing property {property}.");
        JsonElement targetSchema = isArray ? element.GetProperty("items") : element;
        string reference = AssertRef(targetSchema, root);
        Assert.EndsWith(target, reference);
    }

    private static List<JsonElement> CollectBranches(JsonElement root, JsonElement schema)
    {
        var branches = new List<JsonElement> { schema };
        foreach (string keyword in new[] { "anyOf", "oneOf", "allOf" })
        {
            if (schema.TryGetProperty(keyword, out JsonElement options))
                branches.AddRange(options.EnumerateArray());
        }

        var resolved = new List<JsonElement>();
        foreach (JsonElement branch in branches)
        {
            if (branch.ValueKind == JsonValueKind.Object &&
                branch.TryGetProperty("$ref", out JsonElement reference))
                resolved.Add(ResolveRef(root, reference.GetString()!));
            else
                resolved.Add(branch);
        }

        return resolved;
    }

    private static bool AllowsNull(JsonElement schema)
    {
        if (schema.ValueKind != JsonValueKind.Object)
            return false;
        if (schema.TryGetProperty("nullable", out JsonElement nullable) && nullable.GetBoolean())
            return true;
        if (schema.TryGetProperty("type", out JsonElement type))
        {
            if (type.ValueKind == JsonValueKind.String && type.GetString() == "null")
                return true;
            if (type.ValueKind == JsonValueKind.Array &&
                type.EnumerateArray().Any(v => v.GetString() == "null"))
                return true;
        }

        return false;
    }

    private static bool EnumContains(JsonElement branch, JsonElement root, int value)
    {
        if (branch.ValueKind == JsonValueKind.Object &&
            branch.TryGetProperty("$ref", out JsonElement reference))
            branch = ResolveRef(root, reference.GetString()!);
        return branch.ValueKind == JsonValueKind.Object &&
            branch.TryGetProperty("enum", out JsonElement members) &&
            members.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Number && v.GetInt32() == value);
    }

    private static IEnumerable<JsonElement> EnumValues(JsonElement branch)
    {
        if (branch.ValueKind == JsonValueKind.Object &&
            branch.TryGetProperty("enum", out JsonElement members))
        {
            foreach (JsonElement member in members.EnumerateArray())
                yield return member;
        }
    }

    private static void AssertAllowsNull(JsonElement schema, string property)
    {
        JsonElement element = schema.GetProperty("properties").GetProperty(property);
        bool TypeMentionsNull(JsonElement typeElement) =>
            typeElement.ValueKind == JsonValueKind.Array
                ? typeElement.EnumerateArray().Any(v =>
                    v.ValueKind == JsonValueKind.String && v.GetString() == "null")
                : typeElement.GetString() == "null";
        bool allowsNull = (element.TryGetProperty("nullable", out JsonElement nullable) && nullable.GetBoolean()) ||
            (element.TryGetProperty("type", out JsonElement type) && TypeMentionsNull(type)) ||
            (element.TryGetProperty("anyOf", out JsonElement anyOf) &&
                anyOf.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Object &&
                    v.TryGetProperty("type", out JsonElement inner) &&
                    (inner.ValueKind == JsonValueKind.Array
                        ? TypeMentionsNull(inner)
                        : inner.GetString() == "null"))) ||
            (element.TryGetProperty("oneOf", out JsonElement oneOf) &&
                oneOf.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.Object &&
                    v.TryGetProperty("type", out JsonElement innerOne) &&
                    (innerOne.ValueKind == JsonValueKind.Array
                        ? TypeMentionsNull(innerOne)
                        : innerOne.GetString() == "null")));
        Assert.True(allowsNull, $"Property {property} does not allow null.");
    }
}

/// <summary>
/// Representative nonempty nested DTOs serialized with MVC's actual JSON
/// options, matched against the generated schemas: wrapper versus plain IDs,
/// numeric enums, nullable values, and list envelopes. The password-reset
/// sample uses synthetic values; no real reset request is ever issued.
/// </summary>
public sealed class PortalResponseSerializationTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public PortalResponseSerializationTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void StatusResponse_SerializesPlainIdAndNumericEnum()
    {
        using JsonDocument document = JsonDocument.Parse(FetchDocument());
        JsonElement schemas = Schemas(document);
        var sample = new AdminUserStatusResponse(
            Guid.Parse("0198e2c2-4444-7777-8888-000000000001"), UserStatus.Suspended, 2);
        using JsonDocument payload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = payload.RootElement;

        Assert.Equal("0198e2c2-4444-7777-8888-000000000001", json.GetProperty("userId").GetString());
        Assert.Equal(3, json.GetProperty("status").GetInt32());
        Assert.Equal(2, json.GetProperty("revokedSessionCount").GetInt32());
        JsonElement schema = schemas.GetProperty("AdminUserStatusResponse");
        Assert.True(schema.GetProperty("properties").TryGetProperty("userId", out _));
        Assert.Contains(
            json.GetProperty("status").GetInt32(),
            schemas.GetProperty("UserStatus").GetProperty("enum").EnumerateArray().Select(v => v.GetInt32()));
    }

    [Fact]
    public void RegisterResponse_SerializesWrapperIds()
    {
        using JsonDocument document = JsonDocument.Parse(FetchDocument());
        JsonElement schemas = Schemas(document);
        var sample = new Sanad.Modules.Identity.Application.Authentication.Registration.RegisterUserResponse(
            new UserId(Guid.Parse("0198e2c2-4444-7777-8888-000000000001")),
            new VerificationRequestId(Guid.Parse("0198e2c2-4444-7777-8888-000000000002")),
            new VerificationRequestId(Guid.Parse("0198e2c2-4444-7777-8888-000000000003")));
        using JsonDocument payload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = payload.RootElement;

        Assert.Equal(
            "0198e2c2-4444-7777-8888-000000000001",
            json.GetProperty("userId").GetProperty("value").GetString());
        Assert.Equal(
            "0198e2c2-4444-7777-8888-000000000002",
            json.GetProperty("emailVerificationRequestId").GetProperty("value").GetString());
        JsonElement schema = schemas.GetProperty("RegisterUserResponse");
        Assert.Equal(
            "#/components/schemas/UserId",
            schema.GetProperty("properties").GetProperty("userId").GetProperty("$ref").GetString());
    }

    [Fact]
    public void PasswordResetResponse_SerializesSyntheticSample()
    {
        using JsonDocument document = JsonDocument.Parse(FetchDocument());
        JsonElement schemas = Schemas(document);
        var sample = new ResetAdminUserPasswordResponse(
            Guid.Parse("0198e2c2-4444-7777-8888-000000000001"), "Synthetic-Temp-9xQ2!", 1);
        using JsonDocument payload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = payload.RootElement;

        Assert.Equal("Synthetic-Temp-9xQ2!", json.GetProperty("temporaryPassword").GetString());
        JsonElement schema = schemas.GetProperty("ResetAdminUserPasswordResponse");
        Assert.True(schema.GetProperty("properties").TryGetProperty("temporaryPassword", out _));
        Assert.True(schema.GetProperty("properties").TryGetProperty("revokedSessionCount", out _));
    }

    [Fact]
    public void NotificationPage_SerializesEnvelopeWithNullableRead()
    {
        var sample = new AdminNotificationPage(
            [new AdminNotificationRecord(
                Guid.Parse("0198e2c2-4444-7777-8888-000000000001"),
                "Sos", "Created", DateTime.UtcNow, null)],
            1, 20, true);
        using JsonDocument payload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = payload.RootElement;

        Assert.True(json.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("items")[0].GetProperty("readOnUtc").ValueKind);
    }

    [Fact]
    public void Inventory_SerializesNestedEnumsAndNullableReason()
    {
        using JsonDocument document = JsonDocument.Parse(FetchDocument());
        JsonElement schemas = Schemas(document);
        var sample = new CareHomeInventoryDto(
            Guid.Parse("0198e2c2-4444-7777-8888-000000000001"),
            3,
            [new CareHomeRoomTypeDto(
                Guid.Parse("0198e2c2-4444-7777-8888-000000000002"),
                Guid.Parse("0198e2c2-4444-7777-8888-000000000001"),
                "مشتركة", "Shared", 1000m, CareHomeAllocationMode.Shared, false, [], [])],
            [],
            [],
            [new CareHomeMaintenanceDto(
                Guid.Parse("0198e2c2-4444-7777-8888-000000000003"),
                Guid.Parse("0198e2c2-4444-7777-8888-000000000001"),
                CareHomeMaintenanceTarget.Bed,
                Guid.Parse("0198e2c2-4444-7777-8888-000000000004"),
                new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 3), null, false, null, null)]);
        using JsonDocument inventoryPayload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = inventoryPayload.RootElement;

        Assert.Equal(1, json.GetProperty("roomTypes")[0].GetProperty("allocationMode").GetInt32());
        Assert.Equal(2, json.GetProperty("maintenance")[0].GetProperty("target").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("maintenance")[0].GetProperty("reason").ValueKind);
        Assert.Contains(
            1,
            schemas.GetProperty("CareHomeAllocationMode").GetProperty("enum").EnumerateArray().Select(v => v.GetInt32()));
    }

    [Fact]
    public void SosResponse_SerializesNullableCoordinates()
    {
        var sample = new ElderlySosResponse(
            Guid.NewGuid(), Guid.NewGuid(), ElderlySosStatus.Open, true, null, null,
            DateTime.UtcNow, DateTime.UtcNow);
        using JsonDocument sosPayload = JsonDocument.Parse(Serialize(sample));
        JsonElement json = sosPayload.RootElement;

        Assert.Equal(1, json.GetProperty("status").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("latitude").ValueKind);
    }

    private string Serialize<T>(T value)
    {
        JsonSerializerOptions options = _host.Services
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
            .Value.JsonSerializerOptions;
        return JsonSerializer.Serialize(value, options);
    }

    private static JsonElement Schemas(JsonDocument document) =>
        document.RootElement.GetProperty("components").GetProperty("schemas");

    private string FetchDocument()
    {
        using HttpClient client = _host.CreatePortalClient();
        using HttpResponseMessage response = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}

/// <summary>
/// Existing runtime validation for the five required query parameters through
/// the isolated host with the production dispatch pipeline (validators
/// included) and isolated InMemory module databases: valid, unknown, omitted,
/// empty, malformed, and Guid.Empty inputs. Omission behavior is unchanged by
/// the metadata-only required flags.
/// </summary>
public sealed class PortalRequiredQueryRuntimeTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public PortalRequiredQueryRuntimeTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task PublicCities_ValidAndUnknownParents_ReturnMatchingRows()
    {
        await using CaregiversDbContext db = CreateSeededCaregiversDb();
        Guid parent = db.Governorates.Single().Id.Value;
        using var factory = CreateLookupFactory(db);
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage valid = await client.GetAsync($"/api/v1/lookups/cities?governorateId={parent:D}");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using JsonDocument validBody = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        JsonElement city = Assert.Single(validBody.RootElement.EnumerateArray());
        Assert.Equal("City", city.GetProperty("englishName").GetString());

        using HttpResponseMessage unknown = await client.GetAsync(
            "/api/v1/lookups/cities?governorateId=0198e2c2-4444-7777-8888-000000000099");
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        using JsonDocument unknownBody = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.Equal(0, unknownBody.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("/api/v1/lookups/cities", "Api.Validation.Failed")]
    [InlineData("/api/v1/lookups/cities?governorateId=00000000-0000-0000-0000-000000000000", "Api.Validation.Failed")]
    public async Task PublicCities_OmittedOrEmptyParent_ReturnsValidationFailure(string url, string code)
    {
        await using CaregiversDbContext db = CreateSeededCaregiversDb();
        using var factory = CreateLookupFactory(db);
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("/api/v1/lookups/cities?governorateId=")]
    [InlineData("/api/v1/lookups/cities?governorateId=not-a-guid")]
    [InlineData("/api/v1/lookups/areas?cityId=")]
    [InlineData("/api/v1/lookups/areas?cityId=nope")]
    public async Task PublicLookups_MalformedParent_ReturnsBadRequest(string url)
    {
        await using CaregiversDbContext db = CreateSeededCaregiversDb();
        using var factory = CreateLookupFactory(db);
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PublicAreas_ValidAndUnknownParents_ReturnMatchingRows()
    {
        await using CaregiversDbContext db = CreateSeededCaregiversDb();
        Guid parent = db.Cities.Single().Id.Value;
        using var factory = CreateLookupFactory(db);
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage valid = await client.GetAsync($"/api/v1/lookups/areas?cityId={parent:D}");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using JsonDocument validBody = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        JsonElement area = Assert.Single(validBody.RootElement.EnumerateArray());
        Assert.Equal("Area", area.GetProperty("englishName").GetString());

        using HttpResponseMessage unknown = await client.GetAsync(
            "/api/v1/lookups/areas?cityId=0198e2c2-4444-7777-8888-000000000099");
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        using JsonDocument unknownBody = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.Equal(0, unknownBody.RootElement.GetArrayLength());
    }

    [Theory]
    [InlineData("/api/v1/lookups/areas")]
    [InlineData("/api/v1/lookups/areas?cityId=00000000-0000-0000-0000-000000000000")]
    public async Task PublicAreas_OmittedOrEmptyParent_ReturnsValidationFailure(string url)
    {
        await using CaregiversDbContext db = CreateSeededCaregiversDb();
        using var factory = CreateLookupFactory(db);
        using HttpClient client = CreatePortalClient(factory);

        using HttpResponseMessage response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Api.Validation.Failed", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminCities_ValidParent_ReturnsRows_OmittedParent_ReturnsValidationFailure()
    {
        await using CaregiversDbContext db = CreateSeededCaregiversDb();
        Guid parent = db.Governorates.Single().Id.Value;
        using var factory = CreateLookupFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage valid = await client.GetAsync(
            $"/api/v1/admin/lookups/cities?governorateId={parent:D}");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using JsonDocument validBody = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        Assert.Equal(1, validBody.RootElement.GetArrayLength());

        using HttpResponseMessage omitted = await client.GetAsync("/api/v1/admin/lookups/cities");
        Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        using JsonDocument omittedBody = JsonDocument.Parse(await omitted.Content.ReadAsStringAsync());
        Assert.Equal("Api.Validation.Failed", omittedBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminAreas_ValidParent_ReturnsRows_OmittedParent_ReturnsValidationFailure()
    {
        await using CaregiversDbContext db = CreateSeededCaregiversDb();
        Guid parent = db.Cities.Single().Id.Value;
        using var factory = CreateLookupFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage valid = await client.GetAsync(
            $"/api/v1/admin/lookups/areas?cityId={parent:D}");
        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        using JsonDocument validBody = JsonDocument.Parse(await valid.Content.ReadAsStringAsync());
        Assert.Equal(1, validBody.RootElement.GetArrayLength());

        using HttpResponseMessage omitted = await client.GetAsync("/api/v1/admin/lookups/areas");
        Assert.Equal(HttpStatusCode.BadRequest, omitted.StatusCode);
        using JsonDocument omittedBody = JsonDocument.Parse(await omitted.Content.ReadAsStringAsync());
        Assert.Equal("Api.Validation.Failed", omittedBody.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LicenseExpirations_OmittedThrough_ReturnsEstablishedInvalidQuery()
    {
        await using CareHomesDbContext db = CreateCareHomesDb();
        using var factory = CreateLicenseFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage response = await client.GetAsync("/api/v1/admin/care-homes/license-expirations");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("CareHomes.Admin.InvalidQuery", body.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task LicenseExpirations_ValidThrough_ReturnsEmptyPage()
    {
        await using CareHomesDbContext db = CreateCareHomesDb();
        using var factory = CreateLicenseFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage response = await client.GetAsync(
            "/api/v1/admin/care-homes/license-expirations?through=2026-12-31&page=1&pageSize=20");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetProperty("totalCount").GetInt32());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("items").ValueKind);
    }

    private static CaregiversDbContext CreateSeededCaregiversDb()
    {
        var db = new CaregiversDbContext(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        Governorate governorate = Governorate.Create("محافظة", "Governorate");
        City city = City.Create(governorate.Id, "مدينة", "City");
        Area area = Area.Create(city.Id, "حي", "Area");
        db.Governorates.Add(governorate);
        db.Cities.Add(city);
        db.Areas.Add(area);
        db.SaveChanges();
        return db;
    }

    private static CareHomesDbContext CreateCareHomesDb()
    {
        var db = new CareHomesDbContext(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private WebApplicationFactory<Program> CreateLookupFactory(CaregiversDbContext db) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICaregiversDbContext>();
                services.AddSingleton<ICaregiversDbContext>(db);
            }));

    private WebApplicationFactory<Program> CreateLicenseFactory(CareHomesDbContext db) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ICareHomesDbContext>();
                services.AddSingleton<ICareHomesDbContext>(db);
            }));

    private static HttpClient CreatePortalClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private string CreateAccessToken()
    {
        User user = User.Create(
            FullName.Create("مدير"),
            FullName.Create("Admin"),
            Email.Create("portal.superadmin@example.test"),
            PhoneNumber.Create("+201000003002"));
        user.AddAccount(AccountType.SuperAdmin);
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }
}

/// <summary>
/// R2: finance current-rule empty/populated HTTP truth through the unchanged
/// production MVC pipeline on the isolated host.
/// </summary>
public sealed class PortalFinanceCurrentStateTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public PortalFinanceCurrentStateTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task Current_EmptyState_Returns204WithoutBody()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        using var factory = CreateFinanceFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage response = await client.GetAsync(
            "/api/v1/admin/finance/platform-charge-rules/current");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(string.Empty, await response.Content.ReadAsStringAsync());
        Assert.Null(response.Content.Headers.ContentType);
    }

    [Fact]
    public async Task Current_PopulatedState_Returns200Dto()
    {
        await using FinanceDbContext db = CreateFinanceDb();
        db.PlatformChargeRules.Add(PlatformChargeRule.Create(10m, 14m, 1, DateTime.UtcNow.AddDays(-1)));
        await db.SaveChangesAsync();
        using var factory = CreateFinanceFactory(db);
        using HttpClient client = CreatePortalClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage response = await client.GetAsync(
            "/api/v1/admin/finance/platform-charge-rules/current");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(1, body.RootElement.GetProperty("version").GetInt32());
        Assert.True(body.RootElement.TryGetProperty("platformFeeRatePercentage", out _));
    }

    private static FinanceDbContext CreateFinanceDb()
    {
        var db = new FinanceDbContext(new DbContextOptionsBuilder<FinanceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private WebApplicationFactory<Program> CreateFinanceFactory(FinanceDbContext db) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFinanceDbContext>();
                services.AddSingleton<IFinanceDbContext>(db);
            }));

    private static HttpClient CreatePortalClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private string CreateAccessToken()
    {
        User user = User.Create(
            FullName.Create("مدير"),
            FullName.Create("Admin"),
            Email.Create("portal.finance@example.test"),
            PhoneNumber.Create("+201000003003"));
        user.AddAccount(AccountType.SuperAdmin);
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }
}



