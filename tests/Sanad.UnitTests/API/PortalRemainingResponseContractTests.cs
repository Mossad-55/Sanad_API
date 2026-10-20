using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Bookings;
using Sanad.Modules.CareHomes.Application.Inventory;
using Sanad.Modules.CareHomes.Application.Visits;
using Sanad.Modules.CareHomes.Domain.Bookings;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;
using Sanad.Modules.Families.Application.HelpRequests;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Finance.Application;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Notifications.Application.Notifications;

namespace Sanad.UnitTests.API;

/// <summary>
/// FUR-01 evidence for the 29-operation existing-response metadata matrix over the
/// real generated OpenAPI document served by the isolated
/// <see cref="PortalApiMetadataHost"/> (production registration and controllers;
/// no handwritten schema), plus representative HTTP status/shape assertions through
/// deterministic dispatch fakes (no database, provider, migration or seed).
/// Metadata GETs need no business fixtures. One inventory read additionally reuses
/// the existing minimal owner fixture with readback before HTTP.
/// </summary>
public sealed class PortalRemainingResponseContractTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public PortalRemainingResponseContractTests(PortalApiMetadataHost host) => _host = host;

    public static TheoryData<string, string, int, string, string> SuccessOperations => new()
    {
        { "get", "/api/v1/admin/elderly/help-requests/{requestId}", 200, "ref", "ElderlyHelpRequestResponse" },
        { "get", "/api/v1/admin/elderly/help-requests/{requestId}/history", 200, "array", "ElderlyHelpRequestHistoryResponse" },
        { "get", "/api/v1/care-homes/inventory/mine", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/room-types", 200, "ref", "CareHomeInventoryDto" },
        { "put", "/api/v1/care-homes/inventory/mine/room-types/{id}", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/room-types/{id}/media", 201, "ref", "CareHomeRoomTypeMediaDto" },
        { "delete", "/api/v1/care-homes/inventory/mine/room-types/{id}/media/{mediaId}", 204, "none", string.Empty },
        { "post", "/api/v1/care-homes/inventory/mine/room-types/{id}/archive", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/rooms", 200, "ref", "CareHomeInventoryDto" },
        { "put", "/api/v1/care-homes/inventory/mine/rooms/{id}", 200, "ref", "CareHomeInventoryDto" },
        { "post", "/api/v1/care-homes/inventory/mine/rooms/{id}/media", 201, "ref", "CareHomeRoomMediaDto" },
        { "delete", "/api/v1/care-homes/inventory/mine/rooms/{id}/media/{mediaId}", 204, "none", string.Empty },
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
        { "get", "/api/v1/care-homes/check-in-disputes/mine", 200, "ref", "OwnerCareHomeCheckInDisputesResponse" },
        { "get", "/api/v1/care-homes/visits/mine", 200, "array", "CareHomeVisitResponse" },
        { "get", "/api/v1/care-homes/facilities/mine/visit-settings", 200, "ref", "CareHomeVisitSettingsResponse" },
        { "put", "/api/v1/care-homes/facilities/mine/visit-settings", 200, "ref", "CareHomeVisitSettingsResponse" },
        { "get", "/api/v1/care-homes/dashboard/mine", 200, "ref", "CareHomeDashboard" },
        { "get", "/api/v1/care-homes/revenue/mine", 200, "ref", "CareHomeRevenueReport" },
        { "get", "/api/v1/notifications/unread-count", 200, "int", string.Empty },
        { "post", "/api/v1/admin/finance/platform-charge-rules", 201, "guid", string.Empty },
        { "get", "/api/v1/admin/users/statistics", 200, "ref", "AdminUserStatisticsResponse" }
    };

    [Theory]
    [MemberData(nameof(SuccessOperations))]
    public void SuccessOperation_PublishesActualResponseSchema(
        string method, string path, int status, string kind, string schemaName)
    {
        JsonElement root = FetchDocument();
        JsonElement operation = GetOperation(root, path, method);
        Assert.True(
            operation.GetProperty("responses").TryGetProperty(status.ToString(), out JsonElement response),
            $"Missing {status} response for {method.ToUpperInvariant()} {path}.");

        if (kind == "none")
        {
            Assert.False(
                response.TryGetProperty("content", out _),
                $"204 for {method.ToUpperInvariant()} {path} must carry no body schema.");
            return;
        }

        Assert.True(
            TryGetJsonSchema(response, out JsonElement schema),
            $"Missing application/json schema for {method.ToUpperInvariant()} {path} {status}.");

        if (kind == "array")
        {
            Assert.Equal("array", schema.GetProperty("type").GetString());
            Assert.EndsWith(schemaName, AssertRef(schema.GetProperty("items"), root));
        }
        else if (kind == "int")
        {
            Assert.True(
                SchemaTypeContains(schema, "integer"),
                $"Unread-count schema does not expose an integer type: {schema.GetRawText()}.");
            Assert.Equal("int32", schema.GetProperty("format").GetString());
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
    public void UploadActions_PublishNoSpuriousSuccess200()
    {
        JsonElement root = FetchDocument();
        foreach ((string method, string path) in new[]
        {
            ("post", "/api/v1/care-homes/inventory/mine/room-types/{id}/media"),
            ("post", "/api/v1/care-homes/inventory/mine/rooms/{id}/media"),
            ("post", "/api/v1/admin/finance/platform-charge-rules")
        })
        {
            JsonElement responses = GetOperation(root, path, method).GetProperty("responses");
            Assert.False(
                responses.TryGetProperty("200", out _),
                $"Spurious success-200 metadata on {method.ToUpperInvariant()} {path}.");
        }
    }

    [Fact]
    public void NestedResponseRefs_ResolveToActualShapes()
    {
        JsonElement root = FetchDocument();
        JsonElement inventory = ResolveRef(root, "#/components/schemas/CareHomeInventoryDto");
        AssertRefProperty(root, inventory, "roomTypes", "CareHomeRoomTypeDto", isArray: true);
        AssertRefProperty(root, inventory, "rooms", "CareHomeRoomDto", isArray: true);
        AssertRefProperty(root, inventory, "beds", "CareHomeBedDto", isArray: true);
        AssertRefProperty(root, inventory, "maintenance", "CareHomeMaintenanceDto", isArray: true);

        JsonElement visit = ResolveRef(root, "#/components/schemas/CareHomeVisitResponse");
        JsonElement kind = ResolvePropertyRef(root, visit, "kind", "CareHomeVisitKind");
        Assert.Equal(
            new[] { 1, 2 },
            kind.GetProperty("enum").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.Equal(
            new[] { "Prospective", "Resident" },
            kind.GetProperty("x-enumNames").EnumerateArray().Select(v => v.GetString()).ToArray());
        JsonElement status = ResolvePropertyRef(root, visit, "status", "CareHomeVisitStatus");
        Assert.Equal(
            new[] { 1, 2, 3, 4, 5, 6, 7, 8 },
            status.GetProperty("enum").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.Equal(
            new[] { "Pending", "Approved", "Rejected", "FamilyCancelled", "FacilityCancelled", "Expired", "ReschedulePending", "Rescheduled" },
            status.GetProperty("x-enumNames").EnumerateArray().Select(v => v.GetString()).ToArray());

        JsonElement settings = ResolveRef(root, "#/components/schemas/CareHomeVisitSettingsResponse");
        Assert.True(settings.GetProperty("properties").TryGetProperty("operatingHours", out _));
        Assert.True(settings.GetProperty("properties").TryGetProperty("visitingWindows", out _));

        JsonElement dashboard = ResolveRef(root, "#/components/schemas/CareHomeDashboard");
        AssertRefProperty(root, dashboard, "periodRevenue", "CareHomeRevenueTotals", isArray: false);
        JsonElement revenue = ResolveRef(root, "#/components/schemas/CareHomeRevenueReport");
        AssertRefProperty(root, revenue, "totals", "CareHomeRevenueTotals", isArray: false);
        AssertRefProperty(root, revenue, "transactions", "CareHomeRevenueTransaction", isArray: true);

        JsonElement history = ResolveRef(root, "#/components/schemas/ElderlyHelpRequestHistoryResponse");
        Assert.True(history.GetProperty("properties").TryGetProperty("action", out _));
        Assert.True(history.GetProperty("properties").TryGetProperty("status", out _));

        JsonElement booking = ResolveRef(root, "#/components/schemas/CareHomeBookingListItem");
        Assert.True(booking.GetProperty("properties").TryGetProperty("status", out _));
        Assert.True(booking.GetProperty("properties").TryGetProperty("paymentStatus", out _));

        JsonElement disputes = ResolveRef(root, "#/components/schemas/OwnerCareHomeCheckInDisputesResponse");
        JsonElement envelopeProperties = disputes.GetProperty("properties");
        foreach (string field in new[] { "page", "pageSize", "totalCount", "items" })
            Assert.True(envelopeProperties.TryGetProperty(field, out _), $"Owner dispute page is missing {field}.");
        JsonElement items = disputes.GetProperty("properties").GetProperty("items");
        Assert.Equal("array", items.GetProperty("type").GetString());
        string itemRef = AssertRef(items.GetProperty("items"), root);
        Assert.EndsWith("OwnerCareHomeCheckInDispute", itemRef);
        JsonElement disputeSchema = ResolveRef(root, itemRef);
        JsonElement itemProperties = disputeSchema.GetProperty("properties");
        foreach (string field in new[]
        {
            "id", "bookingId", "facilityId", "checkoutOnUtc", "status",
            "openedOnUtc", "effectiveCheckInOnUtc", "resolvedOnUtc",
            "familyReason", "residentArabicName", "residentEnglishName",
            "originalBookingTotalAmount", "originalBookingCurrency"
        })
            Assert.True(itemProperties.TryGetProperty(field, out _),
                $"Owner dispute item schema is missing {field}.");
        foreach (string field in new[]
        {
            "checkoutOnUtc", "familyReason", "residentArabicName", "residentEnglishName",
            "originalBookingTotalAmount", "originalBookingCurrency", "effectiveCheckInOnUtc", "resolvedOnUtc"
        })
        {
            JsonElement property = itemProperties.GetProperty(field);
            bool nullable = property.TryGetProperty("nullable", out JsonElement nullableFlag) && nullableFlag.ValueKind == JsonValueKind.True;
            bool permitsNullUnion = new[] { "anyOf", "oneOf" }.Any(key => property.TryGetProperty(key, out JsonElement union)
                && union.EnumerateArray().Any(option => option.TryGetProperty("type", out JsonElement type) && type.GetString() == "null"));
            Assert.True(nullable || permitsNullUnion, $"Owner dispute field {field} must allow null.");
        }
        foreach (string field in new[] { "evidence", "reason", "resolvedBy", "openedBy" })
            Assert.False(itemProperties.TryGetProperty(field, out _),
                $"Admin-only field {field} leaked into the owner dispute item schema.");
    }

    [Fact]
    public void F2ReadSchemas_PublishTheAddedFieldsWithTheirNullabilityAndEnumConventions()
    {
        JsonElement root = FetchDocument();

        // 1. Owner booking-request list item. It keeps the pre-F2 component ID
        // CareHomeBookingListItem; the F2 additions live on it, while the Family
        // list keeps its own separate row type and publishes no schema.
        JsonElement ownerItem = ResolveRef(root, "#/components/schemas/CareHomeBookingListItem");
        JsonElement ownerProperties = ownerItem.GetProperty("properties");
        foreach (string field in new[]
                 {
                     "id", "facilityId", "roomTypeId", "startDate", "endDate", "status",
                     "paymentStatus", "totalAmount", "currency", "elderlyEnglishName",
                     "assignedRoomId", "assignedBedId", "actualCheckInOnUtc", "actualCheckOutOnUtc",
                     "familyCheckInConfirmedOnUtc", "refundStatus", "refundAmount", "extensionOfBookingId"
                 })
            Assert.True(ownerProperties.TryGetProperty(field, out _),
                $"Owner booking-request item schema lost its pre-existing field {field}.");
        foreach (string field in new[]
                 {
                     "elderlyArabicName", "elderlyAge", "responsibleContactName",
                     "responsibleContactPhone", "responsibleContactRelationship",
                     "careNeedsNotes", "decisionHoldUntilUtc"
                 })
            Assert.True(ownerProperties.TryGetProperty(field, out _),
                $"Owner booking-request item schema is missing {field}.");
        Assert.True(SchemaTypeContains(ownerProperties.GetProperty("elderlyAge"), "integer"),
            $"elderlyAge is not an integer: {ownerProperties.GetProperty("elderlyAge").GetRawText()}");
        Assert.True(SchemaTypeContains(ownerProperties.GetProperty("decisionHoldUntilUtc"), "string"),
            "decisionHoldUntilUtc is not a string date-time.");
        foreach (string field in new[]
                 {
                     "elderlyArabicName", "elderlyAge", "responsibleContactPhone",
                     "responsibleContactRelationship", "careNeedsNotes", "decisionHoldUntilUtc"
                 })
            Assert.True(PermitsNull(ownerProperties.GetProperty(field)),
                $"Owner booking-request field {field} must allow null: {ownerProperties.GetProperty(field).GetRawText()}");
        Assert.False(ownerProperties.TryGetProperty("medicalSnapshotJson", out _),
            "The medical snapshot must not be part of the booking-request list schema.");

        // The owner operation must still resolve to the pre-F2 component ID,
        // and the Family-only row type must not be published for it.
        JsonElement ownerOperation = GetOperation(root, "/api/v1/care-homes/booking-requests/mine", "get");
        Assert.True(
            ownerOperation.GetProperty("responses").TryGetProperty("200", out JsonElement ownerOk),
            "Missing 200 response for GET /api/v1/care-homes/booking-requests/mine.");
        Assert.True(
            TryGetJsonSchema(ownerOk, out JsonElement ownerSchema),
            "Missing application/json schema for GET /api/v1/care-homes/booking-requests/mine 200.");
        Assert.Equal("array", ownerSchema.GetProperty("type").GetString());
        Assert.Equal(
            "CareHomeBookingListItem",
            AssertRef(ownerSchema.GetProperty("items"), root).Split('/')[^1]);
        Assert.False(ownerItem.GetRawText().Contains("CareHomeFamilyBookingListItem", StringComparison.Ordinal),
            "The Family-only row type leaked into the owner booking-request item schema.");

        // The Family booking list keeps its own row type and stays undocumented
        // in the same way it was before F2: no JSON response schema is published
        // for it, and the owner-only additions are absent from that operation.
        JsonElement familyOperation = GetOperation(root, "/api/v1/family/care-home-bookings", "get");
        Assert.True(
            familyOperation.GetProperty("responses").TryGetProperty("200", out JsonElement familyOk),
            "Missing 200 response for GET /api/v1/family/care-home-bookings.");
        Assert.False(TryGetJsonSchema(familyOk, out _),
            "The Family booking list must not start publishing a response schema in F2.");
        Assert.False(
            familyOperation.GetRawText().Contains("CareHomeBookingListItem", StringComparison.Ordinal)
            || familyOperation.GetRawText().Contains("decisionHoldUntilUtc", StringComparison.Ordinal),
            "Owner-only F2 fields leaked into the Family booking list operation.");

        // 2. Admin booking register row.
        JsonElement adminRow = ResolveListItems(root, "/api/v1/admin/care-homes/bookings", "get", out string adminRef);
        Assert.EndsWith("AdminCareHomeBookingListItem", adminRef);
        JsonElement adminProperties = adminRow.GetProperty("properties");
        Assert.True(SchemaTypeContains(adminProperties.GetProperty("taxAmount"), "number"),
            $"taxAmount is not numeric: {adminProperties.GetProperty("taxAmount").GetRawText()}");
        JsonElement extensionLink = adminProperties.GetProperty("extensionOfBookingId");
        Assert.True(SchemaTypeContains(extensionLink, "string"),
            $"extensionOfBookingId is not a uuid string: {extensionLink.GetRawText()}");
        Assert.Equal("uuid", extensionLink.GetProperty("format").GetString());
        Assert.True(PermitsNull(extensionLink),
            $"extensionOfBookingId must allow null: {extensionLink.GetRawText()}");

        // 3. Chat conversation list item.
        JsonElement conversationRow = ResolveListItems(root, "/api/v1/chat/conversations", "get", out string conversationRef);
        Assert.EndsWith("ConversationListItemResponse", conversationRef);
        JsonElement conversationProperties = conversationRow.GetProperty("properties");
        JsonElement scope = ResolvePropertyRef(root, conversationRow, "scope", "ConversationScope");
        Assert.Equal(
            new[] { 0, 1 },
            scope.GetProperty("enum").EnumerateArray().Select(v => v.GetInt32()).ToArray());
        Assert.Equal(
            new[] { "Direct", "Booking" },
            scope.GetProperty("x-enumNames").EnumerateArray().Select(v => v.GetString()).ToArray());
        JsonElement bookingReference = conversationProperties.GetProperty("bookingId");
        Assert.Equal("uuid", bookingReference.GetProperty("format").GetString());
        Assert.True(PermitsNull(bookingReference),
            $"Chat bookingId must allow null: {bookingReference.GetRawText()}");
    }

    private static bool PermitsNull(JsonElement property)
    {
        bool nullable = property.TryGetProperty("nullable", out JsonElement nullableFlag)
            && nullableFlag.ValueKind == JsonValueKind.True;
        bool permitsNullUnion = new[] { "anyOf", "oneOf" }.Any(key =>
            property.TryGetProperty(key, out JsonElement union)
            && union.EnumerateArray().Any(option =>
                option.TryGetProperty("type", out JsonElement type) && type.GetString() == "null"));
        bool nullTypeMember = property.TryGetProperty("type", out JsonElement typeProperty)
            && typeProperty.ValueKind == JsonValueKind.Array
            && typeProperty.EnumerateArray().Any(v => v.ValueKind == JsonValueKind.String && v.GetString() == "null");
        return nullable || permitsNullUnion || nullTypeMember;
    }

    /// <summary>
    /// Resolves the item schema of a paged 200 response ({ items: [ ... ] })
    /// without assuming the envelope's generated schema name.
    /// </summary>
    private static JsonElement ResolveListItems(JsonElement root, string path, string method, out string reference)
    {
        JsonElement operation = GetOperation(root, path, method);
        Assert.True(
            operation.GetProperty("responses").TryGetProperty("200", out JsonElement response),
            $"Missing 200 response for {method.ToUpperInvariant()} {path}.");
        Assert.True(TryGetJsonSchema(response, out JsonElement schema),
            $"Missing application/json schema for {method.ToUpperInvariant()} {path} 200.");
        JsonElement envelope = ResolveRef(root, AssertRef(schema, root));
        JsonElement items = envelope.GetProperty("properties").GetProperty("items");
        Assert.Equal("array", items.GetProperty("type").GetString());
        reference = AssertRef(items.GetProperty("items"), root);
        return ResolveRef(root, reference);
    }

    [Fact]
    public async Task InventoryMine_PopulatedFixture_Returns200InventoryDto()
    {
        DateTime now = DateTime.UtcNow;
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 41);
        await using Sanad.Modules.CareHomes.Infrastructure.Persistence.CareHomesDbContext db =
            CareHomeOwnerPortalFixture.CreateDb();
        Sanad.Modules.CareHomes.Domain.Facilities.CareHomeFacility facility =
            CareHomeOwnerPortalFixture.SeedFacility(db, owner.Id, now, withProfile: false);
        CareHomeOwnerPortalFixture.InventorySeed seed =
            CareHomeOwnerPortalFixture.SeedInventory(db, facility.Id, now, "101", "A");

        Assert.Equal(1, await db.RoomTypes.CountAsync());
        Assert.Equal(facility.Id, (await db.Rooms.SingleAsync(x => x.Id == seed.Room.Id)).FacilityId);

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/inventory/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(facility.Id.Value.ToString("D"), body.RootElement.GetProperty("facilityId").GetString());
        JsonElement type = Assert.Single(body.RootElement.GetProperty("roomTypes").EnumerateArray());
        Assert.Equal(seed.RoomType.Id.ToString("D"), type.GetProperty("id").GetString());
        Assert.Equal((int)CareHomeAllocationMode.Shared, type.GetProperty("allocationMode").GetInt32());
    }

    /// <summary>
    /// F2 local HTTP proof for the owner booking-request read: the real
    /// production pipeline, authorization policy, controller, MediatR dispatch
    /// and query handler over the isolated in-process Care Homes store seeded
    /// with minimal synthetic rows, read back through HTTP only.
    /// </summary>
    [Fact]
    public async Task OwnerBookingRequests_RealHandler_ReturnsStoredReadFieldsAndRerunsFacilityScoping()
    {
        DateTime now = DateTime.UtcNow;
        User owner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 51);
        User otherOwner = CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 52);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facility = CareHomeOwnerPortalFixture.SeedFacility(db, owner.Id, now, withProfile: false);
        CareHomeOwnerPortalFixture.InventorySeed seed =
            CareHomeOwnerPortalFixture.SeedInventory(db, facility.Id, now);
        CareHomeBooking awaiting = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, facility.Id, seed.RoomType.Id, now, 5151, accept: false);
        CareHomeBooking accepted = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, facility.Id, seed.RoomType.Id, now, 5152, accept: true);
        CareHomeFacility otherFacility = CareHomeOwnerPortalFixture.SeedFacility(db, otherOwner.Id, now, withProfile: false);
        CareHomeBooking foreign = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, otherFacility.Id, Guid.NewGuid(), now, 5153, accept: true);
        Assert.NotNull(awaiting.DecisionHoldUntilUtc);

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, owner));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/care-homes/booking-requests/mine");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        string raw = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(raw);
        Assert.Equal(2, body.RootElement.GetArrayLength());
        JsonElement awaitingRow = body.RootElement.EnumerateArray()
            .Single(row => row.GetProperty("id").GetString() == awaiting.Id.ToString("D"));
        JsonElement acceptedRow = body.RootElement.EnumerateArray()
            .Single(row => row.GetProperty("id").GetString() == accepted.Id.ToString("D"));
        Assert.DoesNotContain(foreign.Id.ToString("D"), raw, StringComparison.Ordinal);

        Assert.Equal((int)CareHomeBookingStatus.PaidAwaitingDecision, awaitingRow.GetProperty("status").GetInt32());
        Assert.Equal((int)CareHomeBookingPaymentStatus.Paid, awaitingRow.GetProperty("paymentStatus").GetInt32());
        Assert.Equal(
            awaiting.DecisionHoldUntilUtc,
            awaitingRow.GetProperty("decisionHoldUntilUtc").GetDateTime().ToUniversalTime());
        Assert.Equal("رعاية", awaitingRow.GetProperty("elderlyArabicName").GetString());
        Assert.Equal("Care", awaitingRow.GetProperty("elderlyEnglishName").GetString());
        Assert.Equal(75, awaitingRow.GetProperty("elderlyAge").GetInt32());
        Assert.Equal("Contact", awaitingRow.GetProperty("responsibleContactName").GetString());
        Assert.Equal("+201000000000", awaitingRow.GetProperty("responsibleContactPhone").GetString());
        Assert.Equal(JsonValueKind.Null, awaitingRow.GetProperty("responsibleContactRelationship").ValueKind);
        Assert.Equal(JsonValueKind.Null, awaitingRow.GetProperty("careNeedsNotes").ValueKind);
        Assert.False(awaitingRow.TryGetProperty("medicalSnapshotJson", out _));
        Assert.False(awaitingRow.TryGetProperty("medicalProfile", out _));

        // A decided booking still stores its decision hold, but the read shape
        // exposes the deadline only while a facility decision is pending.
        Assert.NotNull(accepted.DecisionHoldUntilUtc);
        Assert.Equal(JsonValueKind.Null, acceptedRow.GetProperty("decisionHoldUntilUtc").ValueKind);
        Assert.Equal((int)CareHomeBookingStatus.Accepted, acceptedRow.GetProperty("status").GetInt32());

        // The facility-derived filter is unchanged: the other owner sees only
        // its own facility's booking.
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, otherOwner));
        using HttpResponseMessage otherResponse = await client.GetAsync("/api/v1/care-homes/booking-requests/mine");

        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
        using JsonDocument otherBody = JsonDocument.Parse(await otherResponse.Content.ReadAsStringAsync());
        JsonElement otherRow = Assert.Single(otherBody.RootElement.EnumerateArray());
        Assert.Equal(foreign.Id.ToString("D"), otherRow.GetProperty("id").GetString());
    }

    /// <summary>
    /// F2 local HTTP proof for the admin booking register: the same isolated
    /// in-process store with one base booking and its linked extension row.
    /// </summary>
    [Fact]
    public async Task AdminBookingList_RealHandler_ReturnsPersistedTaxAndExtensionLinkOverHttp()
    {
        DateTime now = DateTime.UtcNow;
        User admin = CareHomeOwnerPortalFixture.CreateUser(AccountType.SuperAdmin, 53);
        await using CareHomesDbContext db = CareHomeOwnerPortalFixture.CreateDb();
        CareHomeFacility facility = CareHomeOwnerPortalFixture.SeedFacility(db, UserId.New(), now, withProfile: false);
        CareHomeOwnerPortalFixture.InventorySeed seed =
            CareHomeOwnerPortalFixture.SeedInventory(db, facility.Id, now);
        CareHomeBooking baseBooking = CareHomeOwnerPortalFixture.SeedPaidBooking(
            db, facility.Id, seed.RoomType.Id, now, 5351, accept: true);
        CareHomeBooking extension = CareHomeBooking.CreateExtension(
            baseBooking, baseBooking.FamilyUserId, 1000m, 100m, 65m, 4, now);
        db.Bookings.Add(extension);
        await db.SaveChangesAsync();

        using var factory = CareHomeOwnerPortalFixture.OverrideCareHomes(_host, db);
        using HttpClient client = CareHomeOwnerPortalFixture.CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(_host, admin));

        using HttpResponseMessage response =
            await client.GetAsync("/api/v1/admin/care-homes/bookings?page=1&pageSize=25");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("totalCount").GetInt32());
        JsonElement items = body.RootElement.GetProperty("items");
        JsonElement baseRow = items.EnumerateArray()
            .Single(row => row.GetProperty("bookingId").GetString() == baseBooking.Id.ToString("D"));
        JsonElement extensionRow = items.EnumerateArray()
            .Single(row => row.GetProperty("bookingId").GetString() == extension.Id.ToString("D"));

        Assert.Equal(JsonValueKind.Null, baseRow.GetProperty("extensionOfBookingId").ValueKind);
        Assert.Equal(50m, baseRow.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(baseBooking.Id.ToString("D"), extensionRow.GetProperty("extensionOfBookingId").GetString());
        Assert.Equal(65m, extensionRow.GetProperty("taxAmount").GetDecimal());
        Assert.Equal(extension.TotalAmount, extensionRow.GetProperty("totalAmount").GetDecimal());

        // The extension filter/search behavior is unchanged and the read is
        // still read-only.
        using HttpResponseMessage filtered =
            await client.GetAsync("/api/v1/admin/care-homes/bookings?bookingStatus=1");
        Assert.Equal(HttpStatusCode.OK, filtered.StatusCode);
        using JsonDocument filteredBody = JsonDocument.Parse(await filtered.Content.ReadAsStringAsync());
        Assert.Equal(extension.Id.ToString("D"),
            Assert.Single(filteredBody.RootElement.GetProperty("items").EnumerateArray())
                .GetProperty("bookingId").GetString());
        Assert.Equal(2, await db.Bookings.CountAsync());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task UnreadCount_FakeDispatch_ReturnsBareInteger(int count)
    {
        using var factory = OverrideSender(_host, new CannedSender(unreadCount: count));
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(
                _host, CareHomeOwnerPortalFixture.CreateUser(AccountType.Family, 42)));

        using HttpResponseMessage response = await client.GetAsync("/api/v1/notifications/unread-count");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(count, int.Parse(await response.Content.ReadAsStringAsync(), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task FinanceCreate_FakeDispatch_Returns201Guid()
    {
        Guid created = Guid.Parse("0198e2c2-4444-7777-8888-0000000000a1");
        using var factory = OverrideSender(_host, new CannedSender(createdRuleId: created));
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(
                _host, CareHomeOwnerPortalFixture.CreateUser(AccountType.SuperAdmin, 43)));

        using var content = new StringContent(
            """{"platformFeeRatePercentage":15.00,"taxRatePercentage":14.00,"version":1,"effectiveOnUtc":"2026-10-01T00:00:00Z"}""",
            Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await client.PostAsync("/api/v1/admin/finance/platform-charge-rules", content);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(created, Guid.Parse((await response.Content.ReadAsStringAsync()).Trim('"')));
    }

    [Fact]
    public async Task HelpDetailAndHistory_FakeDispatch_Return200DtoShapes()
    {
        using var factory = OverrideSender(_host, new CannedSender());
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(
                _host, CareHomeOwnerPortalFixture.CreateUser(AccountType.SuperAdmin, 44)));

        Guid requestId = Guid.Parse("0198e2c2-4444-7777-8888-0000000000b1");
        using HttpResponseMessage detail = await client.GetAsync($"/api/v1/admin/elderly/help-requests/{requestId:D}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using JsonDocument detailBody = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal(requestId.ToString("D"), detailBody.RootElement.GetProperty("id").GetString());
        Assert.Equal((int)ElderlyHelpRequestStatus.Pending, detailBody.RootElement.GetProperty("status").GetInt32());

        using HttpResponseMessage history = await client.GetAsync($"/api/v1/admin/elderly/help-requests/{requestId:D}/history");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        using JsonDocument historyBody = JsonDocument.Parse(await history.Content.ReadAsStringAsync());
        JsonElement entry = Assert.Single(historyBody.RootElement.EnumerateArray());
        Assert.Equal((int)ElderlyHelpRequestHistoryAction.Created, entry.GetProperty("action").GetInt32());
    }

    [Fact]
    public async Task OwnerReads_FakeDispatch_Return200DtoShapes()
    {
        using var factory = OverrideSender(_host, new CannedSender());
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(
                _host, CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 45)));

        using HttpResponseMessage bookings = await client.GetAsync("/api/v1/care-homes/booking-requests/mine");
        Assert.Equal(HttpStatusCode.OK, bookings.StatusCode);
        using JsonDocument bookingsBody = JsonDocument.Parse(await bookings.Content.ReadAsStringAsync());
        JsonElement bookingRow = bookingsBody.RootElement[0];
        Assert.Equal((int)CareHomeBookingStatus.PaidAwaitingDecision, bookingRow.GetProperty("status").GetInt32());
        // F2 additive owner-list fields over the real MVC/JSON pipeline.
        Assert.Equal("عربي", bookingRow.GetProperty("elderlyArabicName").GetString());
        Assert.Equal("John Doe", bookingRow.GetProperty("elderlyEnglishName").GetString());
        Assert.Equal(79, bookingRow.GetProperty("elderlyAge").GetInt32());
        Assert.Equal("Responsible Contact", bookingRow.GetProperty("responsibleContactName").GetString());
        Assert.Equal("+201000000000", bookingRow.GetProperty("responsibleContactPhone").GetString());
        Assert.Equal("Daughter", bookingRow.GetProperty("responsibleContactRelationship").GetString());
        Assert.Equal("Care needs notes", bookingRow.GetProperty("careNeedsNotes").GetString());
        Assert.Equal(
            CannedSender.DecisionHoldUntilUtc,
            bookingRow.GetProperty("decisionHoldUntilUtc").GetDateTime().ToUniversalTime());
        Assert.False(bookingRow.TryGetProperty("medicalSnapshotJson", out _));

        using HttpResponseMessage visits = await client.GetAsync("/api/v1/care-homes/visits/mine");
        Assert.Equal(HttpStatusCode.OK, visits.StatusCode);
        using JsonDocument visitsBody = JsonDocument.Parse(await visits.Content.ReadAsStringAsync());
        Assert.Equal((int)CareHomeVisitKind.Prospective, visitsBody.RootElement[0].GetProperty("kind").GetInt32());

        using HttpResponseMessage settings = await client.GetAsync("/api/v1/care-homes/facilities/mine/visit-settings");
        Assert.Equal(HttpStatusCode.OK, settings.StatusCode);
        using JsonDocument settingsBody = JsonDocument.Parse(await settings.Content.ReadAsStringAsync());
        Assert.Equal(50, settingsBody.RootElement.GetProperty("visitorCapacity").GetInt32());

        using var settingsContent = new StringContent(
            """{"expectedVersion":2,"visitorCapacity":50,"operatingHours":[],"visitingWindows":[],"closures":[]}""",
            Encoding.UTF8, "application/json");
        using HttpResponseMessage updated = await client.PutAsync("/api/v1/care-homes/facilities/mine/visit-settings", settingsContent);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);

        using HttpResponseMessage dashboard = await client.GetAsync("/api/v1/care-homes/dashboard/mine");
        Assert.Equal(HttpStatusCode.OK, dashboard.StatusCode);
        using JsonDocument dashboardBody = JsonDocument.Parse(await dashboard.Content.ReadAsStringAsync());
        Assert.True(dashboardBody.RootElement.TryGetProperty("periodRevenue", out _));

        using HttpResponseMessage revenue = await client.GetAsync("/api/v1/care-homes/revenue/mine");
        Assert.Equal(HttpStatusCode.OK, revenue.StatusCode);
        using JsonDocument revenueBody = JsonDocument.Parse(await revenue.Content.ReadAsStringAsync());
        Assert.True(revenueBody.RootElement.TryGetProperty("totals", out _));
    }

    [Fact]
    public async Task InventoryMutations_FakeDispatch_Preserve200_201_204Distinctions()
    {
        using var factory = OverrideSender(_host, new CannedSender());
        using HttpClient client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", CareHomeOwnerPortalFixture.CreateToken(
                _host, CareHomeOwnerPortalFixture.CreateUser(AccountType.CareHomeOwner, 46)));

        using var roomTypeContent = new StringContent(
            """{"expectedVersion":1,"arabicName":"\u0645\u0634\u062a\u0631\u0643\u0629","englishName":"Shared","monthlyPriceEgp":1000,"allocationMode":1,"arabicDescription":null,"englishDescription":null}""",
            Encoding.UTF8, "application/json");
        using HttpResponseMessage created = await client.PostAsync("/api/v1/care-homes/inventory/mine/room-types", roomTypeContent);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);

        using HttpResponseMessage mine = await client.GetAsync("/api/v1/care-homes/inventory/mine");
        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        Guid typeId = Guid.Parse("0198e2c2-4444-7777-8888-0000000000c1");
        using var typeMedia = new MultipartFormDataContent
        {
            { new StringContent("1"), "expectedVersion" },
            { new StringContent("1"), "kind" },
            { new ByteArrayContent([1, 2, 3]), "file", "cover.jpg" }
        };
        using HttpResponseMessage typeUpload = await client.PostAsync(
            $"/api/v1/care-homes/inventory/mine/room-types/{typeId:D}/media", typeMedia);
        Assert.Equal(HttpStatusCode.Created, typeUpload.StatusCode);
        using JsonDocument typeBody = JsonDocument.Parse(await typeUpload.Content.ReadAsStringAsync());
        Assert.True(typeBody.RootElement.TryGetProperty("id", out _));

        Guid roomId = Guid.Parse("0198e2c2-4444-7777-8888-0000000000c2");
        using var roomMedia = new MultipartFormDataContent
        {
            { new StringContent("1"), "expectedVersion" },
            { new StringContent("2"), "kind" },
            { new ByteArrayContent([4, 5, 6]), "file", "room.jpg" }
        };
        using HttpResponseMessage roomUpload = await client.PostAsync(
            $"/api/v1/care-homes/inventory/mine/rooms/{roomId:D}/media", roomMedia);
        Assert.Equal(HttpStatusCode.Created, roomUpload.StatusCode);

        Guid mediaId = Guid.Parse("0198e2c2-4444-7777-8888-0000000000c3");
        using HttpResponseMessage removeType = await client.DeleteAsync(
            $"/api/v1/care-homes/inventory/mine/room-types/{typeId:D}/media/{mediaId:D}?expectedVersion=1");
        Assert.Equal(HttpStatusCode.NoContent, removeType.StatusCode);
        Assert.Equal(string.Empty, await removeType.Content.ReadAsStringAsync());

        using HttpResponseMessage removeRoom = await client.DeleteAsync(
            $"/api/v1/care-homes/inventory/mine/rooms/{roomId:D}/media/{mediaId:D}?expectedVersion=1");
        Assert.Equal(HttpStatusCode.NoContent, removeRoom.StatusCode);
        Assert.Equal(string.Empty, await removeRoom.Content.ReadAsStringAsync());

        using var availabilityContent = new StringContent(
            """{"startDate":"2027-01-01","endDate":"2027-01-20"}""",
            Encoding.UTF8, "application/json");
        using HttpResponseMessage availability = await client.PostAsync(
            "/api/v1/care-homes/inventory/mine/availability", availabilityContent);
        Assert.Equal(HttpStatusCode.OK, availability.StatusCode);
        using JsonDocument availabilityBody = JsonDocument.Parse(await availability.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, availabilityBody.RootElement.ValueKind);
    }

    private JsonElement FetchDocument()
    {
        using HttpClient client = _host.CreatePortalClient();
        using HttpResponseMessage response = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult()).RootElement;
    }

    private static bool SchemaTypeContains(JsonElement schema, string typeName)
    {
        JsonElement type = schema.GetProperty("type");
        if (type.ValueKind == JsonValueKind.String)
            return type.GetString() == typeName;
        return type.ValueKind == JsonValueKind.Array &&
            type.EnumerateArray().Any(v => v.GetString() == typeName);
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

    private static JsonElement ResolvePropertyRef(JsonElement root, JsonElement parent, string property, string target)
    {
        Assert.True(parent.GetProperty("properties").TryGetProperty(property, out JsonElement element), $"Missing property {property}.");
        Assert.True(element.TryGetProperty("$ref", out JsonElement reference), $"Property {property} does not reference a schema.");
        string value = reference.GetString()!;
        Assert.EndsWith(target, value);
        return ResolveRef(root, value);
    }

    private static void AssertRefProperty(JsonElement root, JsonElement parent, string property, string target, bool isArray)
    {
        Assert.True(parent.GetProperty("properties").TryGetProperty(property, out JsonElement element), $"Missing property {property}.");
        JsonElement targetSchema = isArray ? element.GetProperty("items") : element;
        string reference = AssertRef(targetSchema, root);
        Assert.EndsWith(target, reference);
    }

    private static WebApplicationFactory<Program> OverrideSender(PortalApiMetadataHost host, ISender sender) =>
        host.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISender>();
            services.AddSingleton(sender);
        }));

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    /// <summary>
    /// Deterministic dispatch fake: synthetic DTOs only, no database, provider,
    /// migration or seed. Exercises the production MVC pipeline status mapping
    /// (200 DTO, 201 created, empty 204) and JSON serialization.
    /// </summary>
    private sealed class CannedSender(int unreadCount = 0, Guid? createdRuleId = null) : ISender
    {
        internal static readonly DateTime DecisionHoldUntilUtc = new(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);

        private static readonly Guid FacilityId = Guid.Parse("0198e2c2-4444-7777-8888-000000000001");
        private static readonly Guid RoomTypeId = Guid.Parse("0198e2c2-4444-7777-8888-000000000002");
        private static readonly Guid RoomId = Guid.Parse("0198e2c2-4444-7777-8888-000000000003");
        private static readonly Guid BedId = Guid.Parse("0198e2c2-4444-7777-8888-000000000004");
        private static readonly Guid MaintenanceId = Guid.Parse("0198e2c2-4444-7777-8888-000000000005");

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            object result = request switch
            {
                GetMyInventoryQuery => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                CreateRoomTypeCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                UpdateRoomTypeCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                ArchiveRoomTypeCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                CreateRoomCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                RenameRoomCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                ArchiveRoomCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                CreateBedCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                RenameBedCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                ArchiveBedCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                CreateMaintenanceBlockCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                AmendMaintenanceBlockCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                CancelMaintenanceBlockCommand => Result<CareHomeInventoryDto>.Success(SampleInventory()),
                UploadRoomTypeMediaCommand => Result<CareHomeRoomTypeMediaDto>.Success(
                    new(Guid.Parse("0198e2c2-4444-7777-8888-0000000000c3"), CareHomeRoomTypeMediaKind.Cover, 0, "image/jpeg", 3, null)),
                UploadRoomMediaCommand => Result<CareHomeRoomMediaDto>.Success(
                    new(Guid.Parse("0198e2c2-4444-7777-8888-0000000000c3"), CareHomeRoomMediaKind.Gallery, 0, "image/jpeg", 3, null)),
                RemoveRoomTypeMediaCommand => Result.Success(),
                RemoveRoomMediaCommand => Result.Success(),
                GetMyAvailabilityQuery => Result<IReadOnlyList<CareHomeAvailability>>.Success(
                    [new(RoomTypeId, 2, 4, 3, 6)]),
                GetAvailabilityQuery => Result<IReadOnlyList<CareHomeAvailability>>.Success(
                    [new(RoomTypeId, 2, 4, 3, 6)]),
                Sanad.Modules.CareHomes.Application.Bookings.ListOwnerCareHomeBookingsQuery => Result<IReadOnlyList<CareHomeBookingListItem>>.Success(
                    [new(Guid.Parse("0198e2c2-4444-7777-8888-0000000000d1"), FacilityId, RoomTypeId,
                        new DateOnly(2027, 1, 5), new DateOnly(2027, 2, 5), CareHomeBookingStatus.PaidAwaitingDecision,
                        CareHomeBookingPaymentStatus.Paid, 1150m, "EGP", "John Doe",
                        null, null, null, null, null, CareHomeRefundStatus.None, null, null,
                        "عربي", 79, "Responsible Contact", "+201000000000", "Daughter",
                        "Care needs notes", DecisionHoldUntilUtc)]),
                Sanad.Modules.CareHomes.Application.Bookings.ListOwnerCareHomeCheckInDisputesQuery q =>
                    Result<OwnerCareHomeCheckInDisputesResponse>.Success(new(
                        q.Page, q.PageSize, 1,
                        [new(Guid.Parse("0198e2c2-4444-7777-8888-0000000000f5"), Guid.Parse("0198e2c2-4444-7777-8888-0000000000f6"),
                            FacilityId, null, CareHomeCheckInDisputeStatus.Open, new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc),
                            null, null, "Family reason", "Resident Arabic", "Resident English", 1150m, "EGP")])),
                ListOwnerCareHomeVisitsQuery => Result<IReadOnlyList<CareHomeVisitResponse>>.Success([SampleVisit()]),
                GetOwnerCareHomeVisitSettingsQuery => Result<CareHomeVisitSettingsResponse>.Success(SampleSettings()),
                UpdateOwnerCareHomeVisitSettingsCommand => Result<CareHomeVisitSettingsResponse>.Success(SampleSettings()),
                GetOwnerCareHomeDashboardQuery => Result<CareHomeDashboard>.Success(
                    new(DateOnly.FromDateTime(DateTime.UtcNow), 1, 4, 25m, 0, 1, new(1000m, 100m, 50m, 1150m, 0m, 1150m, 1, 0))),
                GetOwnerCareHomeRevenueQuery => Result<CareHomeRevenueReport>.Success(
                    new(new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 31), new(1000m, 100m, 50m, 1150m, 0m, 1150m, 1, 0), [])),
                GetUnreadNotificationCountQuery => Result<int>.Success(unreadCount),
                CreatePlatformChargeRuleCommand => Result<Guid>.Success(
                    createdRuleId ?? Guid.Parse("0198e2c2-4444-7777-8888-0000000000a1")),
                AdminGetElderlyHelpRequestQuery q => Result<ElderlyHelpRequestResponse>.Success(
                    new(q.RequestId, Guid.Parse("0198e2c2-4444-7777-8888-0000000000e1"), ElderlyHelpRequestStatus.Pending,
                        "actor", "action", "need", null, null, DateTime.UtcNow, DateTime.UtcNow)),
                AdminGetElderlyHelpRequestHistoryQuery => Result<IReadOnlyList<ElderlyHelpRequestHistoryResponse>>.Success(
                    [new(Guid.Parse("0198e2c2-4444-7777-8888-0000000000e2"), ElderlyHelpRequestHistoryAction.Created,
                        ElderlyHelpRequestStatus.Pending, null, Guid.Parse("0198e2c2-4444-7777-8888-0000000000e3"), DateTime.UtcNow)]),
                _ => throw new NotSupportedException($"CannedSender has no canned result for {request.GetType().FullName}.")
            };
            return Task.FromResult((TResponse)result);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static CareHomeInventoryDto SampleInventory() => new(
            FacilityId,
            3,
            [new(RoomTypeId, FacilityId, "مشتركة", "Shared", 1000m, CareHomeAllocationMode.Shared, false, [], [])],
            [new(RoomId, FacilityId, RoomTypeId, "101", false, null, null, "غرفة 101", "Room 101", [])],
            [new(BedId, FacilityId, RoomId, "A", false)],
            [new(MaintenanceId, FacilityId, CareHomeMaintenanceTarget.Bed, BedId,
                new DateOnly(2027, 1, 10), new DateOnly(2027, 1, 12), null)]);

        private static CareHomeVisitResponse SampleVisit() => new(
            Guid.Parse("0198e2c2-4444-7777-8888-0000000000f1"), FacilityId,
            Guid.Parse("0198e2c2-4444-7777-8888-0000000000f2"), CareHomeVisitKind.Prospective, null,
            "Portal Visitor", "+201000000000", 2,
            new DateTimeOffset(new DateTime(2027, 1, 10, 8, 0, 0, DateTimeKind.Utc), TimeSpan.Zero),
            new DateTimeOffset(new DateTime(2027, 1, 10, 9, 0, 0, DateTimeKind.Utc), TimeSpan.Zero),
            CareHomeVisitStatus.Pending, null,
            new DateTimeOffset(new DateTime(2027, 1, 9, 8, 0, 0, DateTimeKind.Utc), TimeSpan.Zero),
            null, null, null, null, null, null,
            new DateTimeOffset(new DateTime(2026, 10, 16, 8, 0, 0, DateTimeKind.Utc), TimeSpan.Zero));

        private static CareHomeVisitSettingsResponse SampleSettings() => new(
            FacilityId, 2, 50, [], [], []);
    }
}
