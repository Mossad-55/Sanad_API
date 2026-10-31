using System.Text.Json;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Nullability metadata contract for positional-record response params over the
/// real generated OpenAPI document served by the isolated
/// <see cref="PortalApiMetadataHost"/>: every <c>Nullable&lt;T&gt;</c> or
/// NRT-nullable scalar property must publish nullability in the form a strict
/// OpenAPI consumer resolves — a legacy <c>nullable: true</c> flag or an
/// <c>anyOf</c>/<c>oneOf</c> union with an explicit <c>{"type": "null"}</c>
/// member — and never as a bare <c>type: [x, "null"]</c> array, which strict
/// readers reject. Non-nullable siblings publish no null-permission at all and
/// keep their exact generated scalar shape; the response record shapes
/// themselves are untouched. The value branch of a rewritten union preserves
/// the leaf's scalar type and format.
/// </summary>
public sealed class PortalNullablePositionalRecordContractTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public PortalNullablePositionalRecordContractTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void NullablePositionalRecordParams_AcrossModules_PublishAStrictNullUnion()
    {
        JsonElement root = FetchDocument();

        // Care Homes: the owner check-in dispute row whose strict consumer
        // loop failed on checkoutOnUtc, and the Admin twin of the same family.
        JsonElement ownerDispute = ResolveDisputeItem(root);
        AssertNullableFields(ownerDispute, "OwnerCareHomeCheckInDispute",
            "checkoutOnUtc", "effectiveCheckInOnUtc", "resolvedOnUtc",
            "familyReason", "residentArabicName", "residentEnglishName",
            "originalBookingTotalAmount", "originalBookingCurrency");

        JsonElement adminDispute = ResolveRef(root, "AdminCareHomeCheckInDispute");
        AssertNullableFields(adminDispute, "AdminCareHomeCheckInDispute",
            "checkoutOnUtc", "effectiveCheckInOnUtc", "resolvedOnUtc",
            "originalBookingTotalAmount", "originalBookingCurrency");

        // Community: admin moderation row (Guid? and DateTime? params).
        JsonElement restriction = ResolveRef(root, "AdminCommunityRestrictionListItem");
        AssertNullableFields(restriction, "AdminCommunityRestrictionListItem",
            "revokedBy", "revokedOnUtc");

        // Chat: booking-scoped conversation rows carry a nullable booking id.
        JsonElement conversation = ResolveRef(root, "ConversationListItemResponse");
        AssertNullableFields(conversation, "ConversationListItemResponse", "bookingId");

        // Families: SOS locations are best-effort coordinates.
        JsonElement sos = ResolveRef(root, "ElderlySosResponse");
        AssertNullableFields(sos, "ElderlySosResponse", "latitude", "longitude");

        // The union members themselves: the scalar leaf keeps its type and
        // format, and the other member is the explicit null schema.
        JsonElement checkout = adminDispute.GetProperty("properties").GetProperty("checkoutOnUtc");
        Assert.True(checkout.TryGetProperty("anyOf", out JsonElement union) || checkout.TryGetProperty("oneOf", out union),
            $"checkoutOnUtc must publish a null union: {checkout.GetRawText()}");
        JsonElement[] members = union.EnumerateArray().ToArray();
        Assert.Equal(2, members.Length);
        JsonElement valueMember = members.Single(member =>
            !(member.TryGetProperty("type", out JsonElement memberType) &&
              memberType.ValueKind == JsonValueKind.String &&
              memberType.GetString() == "null"));
        Assert.True(
            valueMember.TryGetProperty("type", out JsonElement valueType) &&
            valueType.ValueKind == JsonValueKind.String &&
            valueType.GetString() == "string",
            $"checkoutOnUtc value branch lost its string type: {valueMember.GetRawText()}");
        Assert.Equal("date-time", valueMember.GetProperty("format").GetString());
        JsonElement nullMember = members.Single(member =>
            member.TryGetProperty("type", out JsonElement memberNullType) &&
            memberNullType.ValueKind == JsonValueKind.String &&
            memberNullType.GetString() == "null");
        Assert.False(nullMember.TryGetProperty("format", out _),
            "The null branch of the checkoutOnUtc union must not carry a format.");
    }

    [Fact]
    public void NonNullablePositionalRecordParams_PublishNoNullPermission()
    {
        JsonElement root = FetchDocument();

        AssertForbidsNull(ResolveRef(root, "AdminCareHomeCheckInDispute"),
            "AdminCareHomeCheckInDispute", "id", "bookingId", "facilityId", "openedOnUtc");
        AssertForbidsNull(ResolveRef(root, "ConversationListItemResponse"),
            "ConversationListItemResponse", "id", "unreadCount");
        AssertForbidsNull(ResolveRef(root, "ElderlySosResponse"),
            "ElderlySosResponse", "id", "elderlyId", "createdOnUtc");
        AssertForbidsNull(ResolveRef(root, "AdminCommunityRestrictionListItem"),
            "AdminCommunityRestrictionListItem", "restrictionId", "authorId",
            "moderatorId", "startsOnUtc");

        // Non-nullable scalar leaves keep their exact single-type shape.
        JsonElement id = ResolveRef(root, "AdminCareHomeCheckInDispute")
            .GetProperty("properties").GetProperty("id");
        Assert.Equal("string", id.GetProperty("type").GetString());
        Assert.Equal("uuid", id.GetProperty("format").GetString());
        Assert.Equal("int32", ResolveRef(root, "ConversationListItemResponse")
            .GetProperty("properties").GetProperty("unreadCount").GetProperty("format").GetString());
    }

    private JsonDocument? _cachedDocument;

    private JsonElement FetchDocument()
    {
        // Each test method gets a fresh class instance from xUnit, so this
        // per-instance cache loads the document at most once per test.
        if (_cachedDocument is null)
        {
            using HttpClient client = _host.CreatePortalClient();
            using HttpResponseMessage response = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            _cachedDocument = JsonDocument.Parse(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        return _cachedDocument.RootElement;
    }

    private static JsonElement ResolveDisputeItem(JsonElement root)
    {
        JsonElement disputes = ResolveRef(root, "OwnerCareHomeCheckInDisputesResponse");
        JsonElement items = disputes.GetProperty("properties").GetProperty("items");
        Assert.Equal("array", items.GetProperty("type").GetString());
        string itemRef = items.GetProperty("items").GetProperty("$ref").GetString()
            ?? throw new InvalidOperationException("Owner dispute items must be a $ref.");
        Assert.EndsWith("OwnerCareHomeCheckInDispute", itemRef, StringComparison.Ordinal);
        return ResolveRef(root, itemRef);
    }

    /// <summary>
    /// A schema component is addressable either by its component name or by a
    /// full fragment reference; both resolve to the same inline definition.
    /// </summary>
    private static JsonElement ResolveRef(JsonElement root, string reference)
    {
        const string prefix = "#/components/schemas/";
        string name = reference.StartsWith(prefix, StringComparison.Ordinal)
            ? reference[prefix.Length..]
            : reference;

        Assert.True(
            root.GetProperty("components").GetProperty("schemas").TryGetProperty(name, out JsonElement schema),
            $"Missing component schema {name}.");
        return schema;
    }

    private static void AssertNullableFields(
        JsonElement schema, string schemaName, params string[] fields)
    {
        JsonElement properties = schema.GetProperty("properties");
        foreach (string field in fields)
        {
            Assert.True(properties.TryGetProperty(field, out JsonElement property),
                $"{schemaName} is missing {field}.");
            Assert.True(
                PublishesStrictNullUnion(property),
                $"{schemaName}.{field} must publish a strict-consumer null union: {property.GetRawText()}");
        }
    }

    private static void AssertForbidsNull(
        JsonElement schema, string schemaName, params string[] fields)
    {
        JsonElement properties = schema.GetProperty("properties");
        foreach (string field in fields)
        {
            Assert.True(properties.TryGetProperty(field, out JsonElement property),
                $"{schemaName} is missing {field}.");

            bool nullableFlag = property.TryGetProperty("nullable", out JsonElement nullable)
                && nullable.ValueKind == JsonValueKind.True;
            bool nullUnion = new[] { "anyOf", "oneOf" }.Any(key =>
                property.TryGetProperty(key, out JsonElement union) &&
                union.EnumerateArray().Any(option =>
                    option.TryGetProperty("type", out JsonElement optionType) &&
                    optionType.ValueKind == JsonValueKind.String &&
                    optionType.GetString() == "null"));
            bool bareNullTypeArray = property.TryGetProperty("type", out JsonElement type) &&
                type.ValueKind == JsonValueKind.Array &&
                type.EnumerateArray().Any(value =>
                    value.ValueKind == JsonValueKind.String && value.GetString() == "null");

            Assert.False(nullableFlag || nullUnion || bareNullTypeArray,
                $"Non-nullable {schemaName}.{field} must not publish null permission: {property.GetRawText()}");
        }
    }

    private static bool PublishesStrictNullUnion(JsonElement property)
    {
        bool nullableFlag = property.TryGetProperty("nullable", out JsonElement nullable)
            && nullable.ValueKind == JsonValueKind.True;
        bool nullUnion = new[] { "anyOf", "oneOf" }.Any(key =>
            property.TryGetProperty(key, out JsonElement union) &&
            union.EnumerateArray().Any(option =>
                option.TryGetProperty("type", out JsonElement optionType) &&
                optionType.ValueKind == JsonValueKind.String &&
                optionType.GetString() == "null"));
        bool bareNullTypeArray = property.TryGetProperty("type", out JsonElement typeProperty) &&
            typeProperty.ValueKind == JsonValueKind.Array &&
            typeProperty.EnumerateArray().Any(value =>
                value.ValueKind == JsonValueKind.String && value.GetString() == "null");
        return (nullableFlag || nullUnion) && !bareNullTypeArray;
    }
}
