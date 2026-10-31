using System.Text.Json;

namespace Ahlena.UnitTests.API;

/// <summary>
/// Portal-report-follow-up W3 contract evidence over the real generated OpenAPI
/// document served by the isolated <see cref="PortalApiMetadataHost"/>
/// (production controllers and OpenAPI registration; no handwritten schema).
/// Covers the W3 annotation pass only: the previously untyped CMS
/// (wellness/splash), care-home-visit, support and auth routes now publish
/// their actual typed success schemas, genuinely bodiless 204 routes publish
/// no body, and every 4xx failure entry publishes the ProblemDetails schema
/// that the [ApiController] client-error mapping produces for status-only
/// annotations. OpenAPI has a single response object per status, so runtime
/// guard paths that answer the same status with an empty body cannot be
/// expressed here; the route guides document those empty-body cases and
/// clients must accept both shapes. The anonymous availability route stays
/// free of auth error metadata. Metadata reads only — no database writes,
/// provider calls or migrations.
/// </summary>
public sealed class PortalCmsVisitsAuthSchemaContractTests : IClassFixture<PortalApiMetadataHost>
{
    private static readonly object DocumentSync = new();

    private static JsonDocument? _cachedDocument;

    private readonly PortalApiMetadataHost _host;

    public PortalCmsVisitsAuthSchemaContractTests(PortalApiMetadataHost host) => _host = host;

    /// <summary>Typed success schemas published by the W3-annotated operations.</summary>
    public static TheoryData<string, string, int, string, string> SuccessOperations => new()
    {
        // Elderly wellness feed/detail (previously untyped).
        { "get", "/api/v1/elderly/wellness-tips", 200, "ref", "WellnessTipPageResponse" },
        { "get", "/api/v1/elderly/wellness-tips/{id}", 200, "ref", "WellnessTipResponse" },

        // Admin wellness list/detail/preview/create/update/publish/archive.
        { "get", "/api/v1/admin/wellness-tips", 200, "ref", "WellnessTipPageResponse" },
        { "get", "/api/v1/admin/wellness-tips/{id}", 200, "ref", "WellnessTipResponse" },
        { "get", "/api/v1/admin/wellness-tips/{id}/preview", 200, "ref", "WellnessTipResponse" },
        { "post", "/api/v1/admin/wellness-tips", 201, "ref", "WellnessTipResponse" },
        { "put", "/api/v1/admin/wellness-tips/{id}", 200, "ref", "WellnessTipResponse" },
        { "post", "/api/v1/admin/wellness-tips/{id}/publish", 200, "ref", "WellnessTipResponse" },
        { "post", "/api/v1/admin/wellness-tips/{id}/archive", 200, "ref", "WellnessTipResponse" },

        // Care-home visits.
        { "get", "/api/v1/care-homes/facilities/{facilityId}/visit-availability", 200, "ref", "CareHomeVisitAvailabilityResponse" },
        { "get", "/api/v1/family/care-home-visits", 200, "array", "CareHomeVisitResponse" },
        { "post", "/api/v1/family/care-home-visits", 200, "ref", "CareHomeVisitResponse" },
        { "get", "/api/v1/family/care-home-visits/eligible-residents", 200, "array", "CareHomeEligibleResidentResponse" },
        { "get", "/api/v1/family/care-home-visits/{visitId}", 200, "ref", "CareHomeVisitResponse" },
        { "post", "/api/v1/family/care-home-visits/{visitId}/cancel", 200, "ref", "CareHomeVisitResponse" },
        { "post", "/api/v1/family/care-home-visits/{visitId}/reschedule", 200, "ref", "CareHomeVisitResponse" },
        { "post", "/api/v1/care-homes/visits/mine/{visitId}/decision", 200, "ref", "CareHomeVisitResponse" },
        { "post", "/api/v1/care-homes/visits/mine/{visitId}/cancel", 200, "ref", "CareHomeVisitResponse" },
        { "get", "/api/v1/care-homes/visits/mine", 200, "array", "CareHomeVisitResponse" },
        { "get", "/api/v1/care-homes/facilities/mine/visit-settings", 200, "ref", "CareHomeVisitSettingsResponse" },
        { "put", "/api/v1/care-homes/facilities/mine/visit-settings", 200, "ref", "CareHomeVisitSettingsResponse" },

        // Auth sessions/identity document reads the app already depends on.
        { "get", "/api/v1/auth/sessions", 200, "ref", "ActiveSessionsResponse" },
        { "get", "/api/v1/auth/identity-document", 200, "ref", "IdentityDocumentResponse" },
        { "put", "/api/v1/auth/identity-document", 200, "ref", "IdentityDocumentResponse" },

        // Signed-in support reads.
        { "get", "/api/v1/support/tickets", 200, "ref", "MySupportTicketsPage" },
        { "get", "/api/v1/support/tickets/{id}", 200, "ref", "MySupportTicketDetail" },

        // Splash admin update.
        { "put", "/api/v1/admin/splash-screens/{id}", 200, "ref", "SplashScreenResponse" }
    };

    /// <summary>
    /// Genuinely bodiless 204 successes in the W3 scope: the annotation (and
    /// the generated document) must carry no body schema.
    /// </summary>
    public static TheoryData<string, string> NoBodyOperations => new()
    {
        { "put", "/api/v1/auth/avatar" },
        { "post", "/api/v1/auth/password/change" },
        { "post", "/api/v1/auth/password/reset/request" },
        { "post", "/api/v1/auth/password/reset" },
        { "post", "/api/v1/auth/sessions/logout" },
        { "post", "/api/v1/auth/sessions/logout-all" },
        { "delete", "/api/v1/auth/sessions/{sessionId}" }
    };

    [Theory]
    [MemberData(nameof(SuccessOperations))]
    public void AnnotatedOperation_PublishesActualSuccessSchema(
        string method, string path, int status, string kind, string schemaName)
    {
        JsonElement root = Document;
        JsonElement operation = GetOperation(root, path, method);
        Assert.True(
            operation.GetProperty("responses").TryGetProperty(status.ToString(), out JsonElement response),
            $"Missing {status} response for {method.ToUpperInvariant()} {path}.");
        Assert.True(
            TryGetJsonSchema(response, out JsonElement schema),
            $"Missing application/json schema for {method.ToUpperInvariant()} {path} {status}.");

        if (kind == "array")
        {
            Assert.Equal("array", schema.GetProperty("type").GetString());
            Assert.EndsWith(
                schemaName,
                AssertRef(schema.GetProperty("items"), root));
            return;
        }

        Assert.EndsWith(schemaName, AssertRef(schema, root));
    }

    [Theory]
    [MemberData(nameof(NoBodyOperations))]
    public void BodilessSuccessOperation_PublishesNoSchema(string method, string path)
    {
        JsonElement operation = GetOperation(Document, path, method);
        Assert.True(
            operation.GetProperty("responses").TryGetProperty("204", out JsonElement noContent),
            $"Missing 204 response for {method.ToUpperInvariant()} {path}.");
        Assert.False(
            noContent.TryGetProperty("content", out _),
            $"A 204 response must not publish a body schema for {method.ToUpperInvariant()} {path}.");
    }

    /// <summary>
    /// Failure metadata over the real generated document. The [ApiController]
    /// client-error mapping publishes a ProblemDetails schema for every 4xx
    /// entry, status-only annotations included, and OpenAPI has a single
    /// response object per status: statuses that some runtime guards also
    /// answer with an empty body (the no-Family `NotFound()` rejects, the
    /// prospective/Resident guard, the JWT challenge and policy denial) are
    /// still documented with the ProblemDetails form. Handler-only coded
    /// failures publish the same schema.
    /// </summary>
    [Fact]
    public void FamilyAndOwnerVisitOperations_PublishTheirFailureContracts()
    {
        JsonElement create = GetOperation(
            Document, "/api/v1/family/care-home-visits", "post").GetProperty("responses");
        Assert.True(
            create.TryGetProperty("200", out _),
            "Missing 200 response for POST /api/v1/family/care-home-visits.");
        foreach (string status in new[] { "400", "404", "409", "401", "403" })
        {
            AssertProblemDetailsSchema(create, status, "POST /api/v1/family/care-home-visits", Document);
        }

        JsonElement list = GetOperation(
            Document, "/api/v1/family/care-home-visits", "get").GetProperty("responses");
        Assert.True(
            list.TryGetProperty("200", out _),
            "Missing 200 response for GET /api/v1/family/care-home-visits.");
        AssertProblemDetailsSchema(list, "404", "GET /api/v1/family/care-home-visits", Document);

        JsonElement residents = GetOperation(
            Document, "/api/v1/family/care-home-visits/eligible-residents", "get").GetProperty("responses");
        Assert.True(
            residents.TryGetProperty("200", out _),
            "Missing 200 response for GET /api/v1/family/care-home-visits/eligible-residents.");
        foreach (string status in new[] { "400", "404" })
        {
            AssertProblemDetailsSchema(residents, status, "GET /api/v1/family/care-home-visits/eligible-residents", Document);
        }

        JsonElement visit = GetOperation(
            Document, "/api/v1/family/care-home-visits/{visitId}", "get").GetProperty("responses");
        Assert.True(
            visit.TryGetProperty("200", out _),
            "Missing 200 response for GET /api/v1/family/care-home-visits/{visitId}.");
        AssertProblemDetailsSchema(visit, "404", "GET /api/v1/family/care-home-visits/{visitId}", Document);

        JsonElement cancel = GetOperation(
            Document, "/api/v1/family/care-home-visits/{visitId}/cancel", "post").GetProperty("responses");
        Assert.True(
            cancel.TryGetProperty("200", out _),
            "Missing 200 response for POST /api/v1/family/care-home-visits/{visitId}/cancel.");
        foreach (string status in new[] { "404", "409" })
        {
            AssertProblemDetailsSchema(cancel, status, "POST /api/v1/family/care-home-visits/{visitId}/cancel", Document);
        }

        JsonElement reschedule = GetOperation(
            Document, "/api/v1/family/care-home-visits/{visitId}/reschedule", "post").GetProperty("responses");
        Assert.True(
            reschedule.TryGetProperty("200", out _),
            "Missing 200 response for POST /api/v1/family/care-home-visits/{visitId}/reschedule.");
        foreach (string status in new[] { "400", "404", "409" })
        {
            AssertProblemDetailsSchema(reschedule, status, "POST /api/v1/family/care-home-visits/{visitId}/reschedule", Document);
        }

        JsonElement decision = GetOperation(
            Document, "/api/v1/care-homes/visits/mine/{visitId}/decision", "post").GetProperty("responses");
        Assert.True(
            decision.TryGetProperty("200", out _),
            "Missing 200 response for POST /api/v1/care-homes/visits/mine/{visitId}/decision.");
        foreach (string status in new[] { "404", "409", "401", "403" })
        {
            AssertProblemDetailsSchema(decision, status, "POST /api/v1/care-homes/visits/mine/{visitId}/decision", Document);
        }
    }

    /// <summary>
    /// Failure metadata over the real generated document: the [ApiController]
    /// client-error mapping publishes a ProblemDetails schema for the mixed
    /// create/update <c>400</c>s (file/binding rejections with a body,
    /// malformed <c>sectionsJson</c> without — OpenAPI cannot express the
    /// latter per path) and for the handler-only <c>404</c>/<c>409</c>s and
    /// the auth answers on the elderly detail read.
    /// </summary>
    [Fact]
    public void WellnessOperations_PublishTheirFailureContracts()
    {
        JsonElement create = GetOperation(
            Document, "/api/v1/admin/wellness-tips", "post").GetProperty("responses");
        Assert.True(
            create.TryGetProperty("201", out _),
            "Missing 201 response for POST /api/v1/admin/wellness-tips.");
        foreach (string status in new[] { "400", "401", "403" })
        {
            AssertProblemDetailsSchema(create, status, "POST /api/v1/admin/wellness-tips", Document);
        }

        JsonElement update = GetOperation(
            Document, "/api/v1/admin/wellness-tips/{id}", "put").GetProperty("responses");
        Assert.True(
            update.TryGetProperty("200", out _),
            "Missing 200 response for PUT /api/v1/admin/wellness-tips/{id}.");
        foreach (string status in new[] { "400", "404", "409", "401", "403" })
        {
            AssertProblemDetailsSchema(update, status, "PUT /api/v1/admin/wellness-tips/{id}", Document);
        }

        JsonElement elderlyDetail = GetOperation(
            Document, "/api/v1/elderly/wellness-tips/{id}", "get").GetProperty("responses");
        Assert.True(
            elderlyDetail.TryGetProperty("200", out _),
            "Missing 200 response for GET /api/v1/elderly/wellness-tips/{id}.");
        foreach (string status in new[] { "404", "401", "403" })
        {
            AssertProblemDetailsSchema(elderlyDetail, status, "GET /api/v1/elderly/wellness-tips/{id}", Document);
        }
    }

    [Fact]
    public void SupportAndSessionReads_PublishTheirFailureContracts()
    {
        JsonElement tickets = GetOperation(
            Document, "/api/v1/support/tickets", "get").GetProperty("responses");
        Assert.True(
            tickets.TryGetProperty("200", out _),
            "Missing 200 response for GET /api/v1/support/tickets.");
        AssertProblemDetailsSchema(tickets, "401", "GET /api/v1/support/tickets", Document);

        JsonElement ticket = GetOperation(
            Document, "/api/v1/support/tickets/{id}", "get").GetProperty("responses");
        Assert.True(
            ticket.TryGetProperty("200", out _),
            "Missing 200 response for GET /api/v1/support/tickets/{id}.");
        AssertProblemDetailsSchema(ticket, "404", "GET /api/v1/support/tickets/{id}", Document);

        JsonElement sessions = GetOperation(
            Document, "/api/v1/auth/sessions", "get").GetProperty("responses");
        Assert.True(
            sessions.TryGetProperty("200", out _),
            "Missing 200 response for GET /api/v1/auth/sessions.");
        AssertProblemDetailsSchema(sessions, "401", "GET /api/v1/auth/sessions", Document);
    }

    /// <summary>
    /// The avatar download is a binary file response: the 200 answers declare
    /// exactly the accepted upload image media types and every content media
    /// entry carries no object schema — the operation never references the
    /// FileContentResult model (registered globally by other file-download
    /// routes). The not-configured avatar keeps the ProblemDetails 404 that
    /// the client-error mapping documents.
    /// </summary>
    [Fact]
    public void AvatarDownload_PublishesBinaryImageContentAndMissingAvatarFailure()
    {
        JsonElement root = Document;
        JsonElement operation = GetOperation(root, "/api/v1/auth/avatar", "get");
        JsonElement responses = operation.GetProperty("responses");

        Assert.True(
            responses.TryGetProperty("200", out JsonElement ok),
            "Missing 200 response for GET /api/v1/auth/avatar.");
        JsonElement content = ok.GetProperty("content");
        foreach (string mediaType in new[] { "image/jpeg", "image/png", "image/webp" })
        {
            Assert.True(
                content.TryGetProperty(mediaType, out _),
                $"Missing {mediaType} content for GET /api/v1/auth/avatar.");
        }

        foreach (JsonProperty mediaEntry in content.EnumerateObject())
        {
            Assert.False(
                mediaEntry.Value.TryGetProperty("schema", out _),
                $"The {mediaEntry.Name} avatar content must stay binary with no object schema.");
        }

        Assert.True(
            responses.TryGetProperty("404", out _),
            "Missing 404 response for GET /api/v1/auth/avatar.");
        AssertProblemDetailsSchema(responses, "404", "GET /api/v1/auth/avatar", root);
        AssertProblemDetailsSchema(responses, "401", "GET /api/v1/auth/avatar", root);
    }

    /// <summary>
    /// The anonymous availability route must not document 401/403: it is the
    /// public pre-login slot read, like the platform status surface.
    /// </summary>
    [Fact]
    public void AnonymousAvailability_DocumentsNeitherAuthError()
    {
        JsonElement responses = GetOperation(
            Document, "/api/v1/care-homes/facilities/{facilityId}/visit-availability", "get")
            .GetProperty("responses");
        Assert.True(
            responses.TryGetProperty("200", out _),
            "Missing 200 response for the visit availability read.");
        foreach (string status in new[] { "401", "403" })
        {
            Assert.False(
                responses.TryGetProperty(status, out _),
                $"The anonymous availability route must not document {status}.");
        }
    }

    [Fact]
    public void SplashUpdate_PublishesItsFailureContract()
    {
        JsonElement responses = GetOperation(
            Document, "/api/v1/admin/splash-screens/{id}", "put").GetProperty("responses");
        Assert.True(
            responses.TryGetProperty("200", out _),
            "Missing 200 response for PUT /api/v1/admin/splash-screens/{id}.");
        foreach (string status in new[] { "400", "404", "401", "403" })
        {
            AssertProblemDetailsSchema(responses, status, "PUT /api/v1/admin/splash-screens/{id}", Document);
        }
    }

    /// <summary>
    /// The [ApiController] client-error mapping documents ProblemDetails for
    /// every 4xx entry, status-only annotations included, and OpenAPI has a
    /// single response object per status: where a runtime guard answers the
    /// same status with an empty body, the generated contract still carries
    /// the ProblemDetails form and that per-path difference cannot be
    /// expressed here (the route guides document the empty-body cases).
    /// </summary>
    private static void AssertProblemDetailsSchema(
        JsonElement responses, string status, string operationLabel, JsonElement root)
    {
        Assert.True(
            responses.TryGetProperty(status, out JsonElement response),
            $"Missing {status} response for {operationLabel}.");
        Assert.True(
            TryGetAnyResponseSchema(response, out JsonElement schema),
            $"The {status} entry on {operationLabel} must publish a schema in its content media types.");
        Assert.EndsWith(
            "ProblemDetails",
            AssertRef(schema, root));
    }

    /// <summary>
    /// Failure metadata may be published under <c>application/problem+json</c>
    /// rather than <c>application/json</c>, so schema detection scans every
    /// content media type.
    /// </summary>
    private static bool TryGetAnyResponseSchema(JsonElement response, out JsonElement schema)
    {
        schema = default;
        if (!response.TryGetProperty("content", out JsonElement content))
        {
            return false;
        }

        foreach (JsonProperty media in content.EnumerateObject())
        {
            if (media.Value.TryGetProperty("schema", out schema))
            {
                return true;
            }
        }

        return false;
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
}
