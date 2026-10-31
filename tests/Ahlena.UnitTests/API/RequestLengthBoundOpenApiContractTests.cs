using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Pins the request-schema length metadata to EXACTLY the domain measures.
/// Only the visit-settings closure reason is annotated (`maxLength: 500`):
/// `CareHomeVisitSettings.Validate` measures the RAW JSON value before
/// trimming, so the annotation is an exact, acceptance-preserving cap.
/// The visit decision/cancellation reasons and the support reply body are
/// capped on the TRIMMED (consumed) text by
/// <c>CareHomeVisit.Reject</c>/<c>CareHomeVisit.Cancel</c> (1000) and
/// <c>SupportTicketReply.Create</c> (1–2000); a raw <c>maxLength</c> would
/// falsely reject whitespace-padded values and approve-ignored reasons, so
/// their schemas pin NO <c>maxLength</c> and the caps stay documented. The
/// care-home booking decision/cancellation reason, the room-type bilingual
/// descriptions and the maintenance reason have no length rule at all and
/// equally pin no <c>maxLength</c>.
/// </summary>
public sealed class RequestLengthBoundOpenApiContractTests : IClassFixture<PortalApiMetadataHost>
{
    private static readonly object DocumentSync = new();

    private static JsonDocument? _cachedDocument;

    private readonly PortalApiMetadataHost _host;

    public RequestLengthBoundOpenApiContractTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void VisitSettingsClosureReason_PublishesTheExactRawValueMaxLength500()
    {
        JsonElement root = Document;
        JsonElement requestSchema = ResolveRequestSchema(
            root, "/api/v1/care-homes/facilities/mine/visit-settings", "put");

        // Settings carry `closures` as an array of the closure DTO whose
        // `reason` the domain bounds at 500 characters measured on the raw
        // value before trimming — exactly what `maxLength: 500` claims.
        JsonElement closures = ResolveProperty(requestSchema, "closures");
        Assert.Equal("array", closures.GetProperty("type").GetString());
        JsonElement closureSchema = ResolveRef(
            root, closures.GetProperty("items").GetProperty("$ref").GetString()!);
        JsonElement reason = StringValueBranch(ResolveProperty(closureSchema, "reason"));

        Assert.Equal("string", reason.GetProperty("type").GetString());
        Assert.Equal(500, reason.GetProperty("maxLength").GetInt32());
    }

    public static TheoryData<string, string, string> NoMaxLengthReasonFields => new()
    {
        // Not annotated on purpose — the domain measures the TRIMMED text at
        // 1000 (CareHomeVisit.Reject / CareHomeVisit.Cancel), and the approve
        // path ignores the decision reason entirely. A raw maxLength would
        // reject previously accepted whitespace-padded / ignored values.
        { "post", "/api/v1/care-homes/visits/mine/{visitId}/decision", "reason" },
        { "post", "/api/v1/care-homes/visits/mine/{visitId}/cancel", "reason" },
        { "post", "/api/v1/family/care-home-visits/{visitId}/cancel", "reason" },
        // Trimmed 1–2000 in SupportTicketReply.Create — same false-cap issue.
        { "post", "/api/v1/admin/support-tickets/{id}/replies", "body" },
        // No length rule anywhere in API or domain: the DB `text` /
        // varchar columns are the only backstop.
        { "post", "/api/v1/care-homes/booking-requests/mine/{bookingId}/decision", "reason" },
        { "post", "/api/v1/care-homes/booking-requests/mine/{bookingId}/cancel", "reason" },
        { "post", "/api/v1/care-homes/inventory/mine/room-types", "arabicDescription" },
        { "post", "/api/v1/care-homes/inventory/mine/room-types", "englishDescription" },
        { "put", "/api/v1/care-homes/inventory/mine/room-types/{id}", "arabicDescription" },
        { "put", "/api/v1/care-homes/inventory/mine/room-types/{id}", "englishDescription" },
        { "post", "/api/v1/care-homes/inventory/mine/maintenance", "reason" },
        { "put", "/api/v1/care-homes/inventory/mine/maintenance/{id}", "reason" }
    };

    [Theory]
    [MemberData(nameof(NoMaxLengthReasonFields))]
    public void TrimmedOrUnboundedReason_PublishesNoRawMaxLength(string method, string path, string property)
    {
        JsonElement root = Document;
        JsonElement requestSchema = ResolveRequestSchema(root, path, method);
        JsonElement propertySchema = ResolveProperty(requestSchema, property);

        foreach (JsonElement branch in ValueBranches(propertySchema))
        {
            Assert.False(
                branch.TryGetProperty("maxLength", out _),
                $"{method.ToUpperInvariant()} {path}: {property} must not publish a raw maxLength.");
            Assert.False(
                branch.TryGetProperty("minLength", out _),
                $"{method.ToUpperInvariant()} {path}: {property} must not publish a raw minLength.");
        }
    }

    [Fact]
    public void SupportReplyBody_StaysRequiredAndPreservesTheDocumentedTrimmedBound()
    {
        JsonElement root = Document;
        JsonElement requestSchema = ResolveRequestSchema(
            root, "/api/v1/admin/support-tickets/{id}/replies", "post");

        // Requiredness is preserved: the record's non-nullable Body stays in
        // the schema's required list.
        JsonElement required = requestSchema.GetProperty("required");
        Assert.True(
            required.EnumerateArray().Any(name => name.GetString() == "body"),
            "ReplyToSupportTicketRequest.body must stay required.");

        // The 1–2000 rule is documented on the consumed (trimmed) text, not a
        // raw JSON cap; no raw minLength may appear (whitespace-padded accepted
        // bodies can have a raw length above the trimmed minimum).
        JsonElement body = requestSchema.GetProperty("properties").GetProperty("body");
        foreach (JsonElement branch in ValueBranches(body))
        {
            Assert.False(branch.TryGetProperty("minLength", out _), "body must not publish a raw minLength.");
        }
    }

    private JsonElement Document
    {
        get
        {
            lock (DocumentSync)
            {
                if (_cachedDocument is null)
                {
                    using HttpClient client = _host.CreateClient();
                    using HttpResponseMessage response = client
                        .GetAsync("/openapi/v1.json")
                        .GetAwaiter()
                        .GetResult();
                    Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
                    _cachedDocument = JsonDocument.Parse(
                        response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
                }

                return _cachedDocument.RootElement;
            }
        }
    }

    private static JsonElement ResolveRequestSchema(JsonElement root, string path, string method)
    {
        JsonElement operation = root
            .GetProperty("paths")
            .GetProperty(path)
            .GetProperty(method);
        Assert.True(
            operation.TryGetProperty("requestBody", out JsonElement requestBody),
            $"Missing requestBody on {method.ToUpperInvariant()} {path}.");
        JsonElement schema = requestBody
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema");
        Assert.True(
            schema.TryGetProperty("$ref", out JsonElement reference),
            $"Expected a $ref request schema on {method.ToUpperInvariant()} {path}.");
        return ResolveRef(root, reference.GetString()!);
    }

    private static JsonElement ResolveProperty(JsonElement schema, string property) =>
        schema.GetProperty("properties").GetProperty(property);

    /// <summary>
    /// The single non-null string branch of a property schema: nullable
    /// strings arrive as the anyOf union written by
    /// <c>PortalNullableSchemaUnionTransformer</c> (value branch plus a null
    /// branch), other strings as an inline schema.
    /// </summary>
    private static JsonElement StringValueBranch(JsonElement propertySchema) =>
        ValueBranches(propertySchema).Single();

    private static IEnumerable<JsonElement> ValueBranches(JsonElement schema)
    {
        if (schema.TryGetProperty("anyOf", out JsonElement anyOf))
        {
            foreach (JsonElement branch in anyOf.EnumerateArray())
            {
                if (!IsNullBranch(branch))
                {
                    yield return branch;
                }
            }

            yield break;
        }

        yield return schema;
    }

    private static bool IsNullBranch(JsonElement schema) =>
        schema.TryGetProperty("type", out JsonElement type)
        && type.ValueKind == JsonValueKind.String
        && type.GetString() == "null";

    private static JsonElement ResolveRef(JsonElement root, string reference)
    {
        Assert.StartsWith("#/components/schemas/", reference, StringComparison.Ordinal);
        return root
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty(reference["#/components/schemas/".Length..]);
    }
}
