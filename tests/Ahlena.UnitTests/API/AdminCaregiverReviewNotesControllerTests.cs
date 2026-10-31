using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// E07 caregiver review notes at the HTTP boundary. Every request goes through
/// the real controller, the reused CaregiverReviewAdmin policy, MediatR and the
/// real handlers against an isolated in-memory Caregivers store; only that store
/// is replaced, so the runtime pipeline, the local problem-details mapping and
/// the generated OpenAPI document are the production ones. The tests pin the two
/// routes, the policy, the request shape (text only — never a client-supplied
/// author or timestamp), the 200/201/400/404 outcomes with their business codes,
/// the role denial for ContentAdmin, Family, Caregiver, Elderly, FinanceAdmin and
/// Care Home owners, the anonymous 401, clamped paging with the full count, and
/// the append-only guarantee: no update, patch or delete verb exists, no 409 or
/// 5xx path is published, and no malformed or hostile input produces a server
/// error. All data is synthetic.
/// </summary>
public sealed class AdminCaregiverReviewNotesControllerTests : IClassFixture<PortalApiMetadataHost>
{
    private const string NotesRoute = "api/v1/admin/caregivers";
    private const string NotesPath = "/api/v1/admin/caregivers/{caregiverId}/notes";

    private static readonly string[] ForbiddenTokens =
    [
        "phone", "email", "avatar", "identity", "document", "medical", "address",
        "national", "birth", "displayname", "reviewername"
    ];

    private static readonly string[] HttpVerbs =
        ["get", "put", "post", "delete", "options", "head", "patch", "trace"];

    private readonly PortalApiMetadataHost _host;
    private JsonDocument? _document;

    public AdminCaregiverReviewNotesControllerTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public void Controller_PinsThePolicyTheRouteAndBothActionTemplates()
    {
        AuthorizeAttribute authorize = Assert.Single(
            typeof(AdminCaregiversController).GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.CaregiverReviewAdmin, authorize.Policy);
        Assert.Equal(
            NotesRoute,
            Assert.Single(
                typeof(AdminCaregiversController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Empty(
            typeof(AdminCaregiversController).GetCustomAttributes<AllowAnonymousAttribute>());

        AssertActionTemplate(nameof(AdminCaregiversController.GetReviewNotes), "{caregiverId:guid}/notes", "GET");
        AssertActionTemplate(nameof(AdminCaregiversController.AddReviewNote), "{caregiverId:guid}/notes", "POST");
    }

    [Fact]
    public void RequestBody_CarriesOnlyTheTextSoNoClientSuppliesTheAuthorOrTheInstant()
    {
        Assert.Equal(
            ["Text"],
            typeof(AddCaregiverReviewNoteRequest)
                .GetProperties()
                .Select(property => property.Name));

        ParameterInfo body = typeof(AdminCaregiversController)
            .GetMethod(nameof(AdminCaregiversController.AddReviewNote))!
            .GetParameters()
            .Single(parameter => parameter.GetCustomAttribute<FromBodyAttribute>() is not null);

        Assert.Equal(typeof(AddCaregiverReviewNoteRequest), body.ParameterType);
    }

    [Fact]
    public void TheControllerPublishesNoUpdatePatchOrDeleteActionForANote()
    {
        string[] verbs =
        [
            .. typeof(AdminCaregiversController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(method => method.GetCustomAttributes<HttpMethodAttribute>()
                    .Any(attribute => (attribute.Template ?? string.Empty)
                        .EndsWith("/notes", StringComparison.Ordinal)))
                .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
                .Select(attribute => Assert.Single(attribute.HttpMethods))
                .OrderBy(verb => verb, StringComparer.Ordinal)
        ];

        Assert.Equal(["GET", "POST"], verbs);
    }

    [Fact]
    public void Actions_PublishOnlyThePinnedStatusCodesAndNeverAConflictOrServerError()
    {
        Assert.Equal<int>(
            [200, 404],
            PublishedStatusCodes(nameof(AdminCaregiversController.GetReviewNotes)));
        Assert.Equal<int>(
            [201, 400, 404],
            PublishedStatusCodes(nameof(AdminCaregiversController.AddReviewNote)));

        foreach (string action in new[]
                 {
                     nameof(AdminCaregiversController.GetReviewNotes),
                     nameof(AdminCaregiversController.AddReviewNote)
                 })
        {
            Assert.DoesNotContain(PublishedStatusCodes(action), status => status == 409);
            Assert.DoesNotContain(PublishedStatusCodes(action), status => status >= 500);
        }
    }

    [Fact]
    public void OpenApi_PublishesBothNoteRoutesAndNoNoteSubResource()
    {
        JsonElement root = Document();

        Assert.Equal<string>(["get", "post"], PathMethods(root, NotesPath));
        Assert.DoesNotContain(
            root.GetProperty("paths").EnumerateObject().Select(path => path.Name),
            path => path.StartsWith($"{NotesPath}/", StringComparison.Ordinal));
    }

    [Fact]
    public void OpenApi_PublishesTheNoteShapesTheQueryParametersAndNo409Or5xx()
    {
        JsonElement root = Document();
        JsonElement list = GetOperation(root, NotesPath, "get");
        JsonElement add = GetOperation(root, NotesPath, "post");

        string[] queryParameters =
        [
            .. list.GetProperty("parameters")
                .EnumerateArray()
                .Where(parameter => parameter.GetProperty("in").GetString() == "query")
                .Select(parameter => parameter.GetProperty("name").GetString()!)
                .OrderBy(name => name, StringComparer.Ordinal)
        ];
        Assert.Equal(["page", "pageSize"], queryParameters);

        Assert.EndsWith(
            "AdminCaregiverReviewNotePage",
            AssertRef(list.GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root));
        Assert.EndsWith(
            "AdminCaregiverReviewNoteItem",
            AssertRef(add.GetProperty("responses").GetProperty("201")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root));
        Assert.EndsWith(
            "AddCaregiverReviewNoteRequest",
            AssertRef(add.GetProperty("requestBody")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root));

        AssertExactProperties(
            ResolveRef(root, "#/components/schemas/AdminCaregiverReviewNoteItem").GetProperty("properties"),
            "noteId", "caregiverId", "text", "authorUserId", "createdOnUtc");
        AssertExactProperties(
            ResolveRef(root, "#/components/schemas/AdminCaregiverReviewNotePage").GetProperty("properties"),
            "items", "page", "pageSize", "totalCount");
        AssertExactProperties(
            ResolveRef(root, "#/components/schemas/AddCaregiverReviewNoteRequest").GetProperty("properties"),
            "text");

        foreach (string schema in new[]
                 {
                     "AdminCaregiverReviewNoteItem", "AdminCaregiverReviewNotePage",
                     "AddCaregiverReviewNoteRequest"
                 })
        {
            Assert.DoesNotContain(
                ResolveRef(root, $"#/components/schemas/{schema}")
                    .GetProperty("properties")
                    .EnumerateObject()
                    .Select(property => property.Name),
                name => ForbiddenTokens.Any(
                    token => name.Contains(token, StringComparison.OrdinalIgnoreCase)));
        }

        Assert.True(list.GetProperty("responses").TryGetProperty("404", out _));
        Assert.True(add.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(add.GetProperty("responses").TryGetProperty("404", out _));

        foreach (JsonElement operation in new[] { list, add })
        {
            Assert.DoesNotContain(
                operation.GetProperty("responses").EnumerateObject().Select(response => response.Name),
                status => status == "409" || status.StartsWith('5'));
        }
    }

    [Fact]
    public async Task BothRoutes_RejectAnAnonymousCaller()
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            using HttpClient client = CreateClient(factory);
            string url = NotesUrl(caregiver.Id.Value);

            using HttpResponseMessage list = await client.GetAsync(url);
            using HttpResponseMessage add = await client.PostAsync(url, NoteBody("anonymous attempt"));

            Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, add.StatusCode);
            Assert.Empty(await db.ReviewNotes.ToListAsync());
        }
    }

    [Theory]
    [InlineData(AccountType.ContentAdmin)]
    [InlineData(AccountType.Family)]
    [InlineData(AccountType.MedicalCaregiver)]
    [InlineData(AccountType.CompanionCaregiver)]
    [InlineData(AccountType.Elderly)]
    [InlineData(AccountType.FinanceAdmin)]
    [InlineData(AccountType.CareHomeOwner)]
    public async Task BothRoutes_DenyEveryAccountTypeOutsideTheReviewPolicy(AccountType accountType)
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(accountType, 31)));
            string url = NotesUrl(caregiver.Id.Value);

            using HttpResponseMessage list = await client.GetAsync(url);
            using HttpResponseMessage add = await client.PostAsync(url, NoteBody("denied attempt"));

            Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
            Assert.Empty(await db.ReviewNotes.ToListAsync());
        }
    }

    [Theory]
    [InlineData(AccountType.SuperAdmin)]
    [InlineData(AccountType.SupportAdmin)]
    public async Task BothRoutes_AllowExactlyTheTwoReviewPoliciesAccountTypes(AccountType accountType)
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(accountType, 41)));

            using HttpResponseMessage add = await client.PostAsync(
                NotesUrl(caregiver.Id.Value), NoteBody("allowed attempt"));
            using HttpResponseMessage list = await client.GetAsync(NotesUrl(caregiver.Id.Value));

            Assert.Equal(HttpStatusCode.Created, add.StatusCode);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);

            using JsonDocument readback = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            Assert.Equal(1, readback.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(
                "allowed attempt",
                Assert.Single(readback.RootElement.GetProperty("items").EnumerateArray())
                    .GetProperty("text")
                    .GetString());
        }
    }

    [Fact]
    public async Task Post_CreatesOneNoteAndTheListReadsBackExactlyWhatWasStored()
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            User admin = CreateUser(AccountType.SupportAdmin, 51);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(admin));
            string url = NotesUrl(caregiver.Id.Value);

            using HttpResponseMessage empty = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using (JsonDocument emptyBody = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()))
            {
                Assert.Empty(emptyBody.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(0, emptyBody.RootElement.GetProperty("totalCount").GetInt32());
            }

            using HttpResponseMessage created = await client.PostAsync(
                url, NoteBody("   Retained E07 verification note.   "));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);

            string createdBody = await created.Content.ReadAsStringAsync();
            Guid noteId;
            DateTime createdOnUtc;
            using (JsonDocument body = JsonDocument.Parse(createdBody))
            {
                JsonElement root = body.RootElement;
                AssertExactJsonProperties(
                    root, "noteId", "caregiverId", "text", "authorUserId", "createdOnUtc");
                Assert.True(Guid.TryParse(root.GetProperty("noteId").GetString(), out noteId));
                Assert.NotEqual(Guid.Empty, noteId);
                Assert.Equal(
                    caregiver.Id.Value.ToString("D"),
                    root.GetProperty("caregiverId").GetString());
                Assert.Equal("Retained E07 verification note.", root.GetProperty("text").GetString());
                Assert.Equal(admin.Id.Value.ToString("D"), root.GetProperty("authorUserId").GetString());

                createdOnUtc = root.GetProperty("createdOnUtc").GetDateTime();
                Assert.Equal(DateTimeKind.Utc, createdOnUtc.Kind);
                Assert.True(
                    Math.Abs((DateTime.UtcNow - createdOnUtc).TotalMinutes) < 5,
                    "the stored creation instant must come from the server clock, not from the request");
            }

            Assert.DoesNotContain(
                ForbiddenTokens,
                token => createdBody.Contains(token, StringComparison.OrdinalIgnoreCase));

            using HttpResponseMessage listed = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, listed.StatusCode);
            using (JsonDocument page = JsonDocument.Parse(await listed.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1, page.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(noteId.ToString("D"), item.GetProperty("noteId").GetString());
                Assert.Equal(
                    "Retained E07 verification note.", item.GetProperty("text").GetString());
                Assert.Equal(admin.Id.Value.ToString("D"), item.GetProperty("authorUserId").GetString());
                Assert.Equal(createdOnUtc, item.GetProperty("createdOnUtc").GetDateTime());
            }

            // The same text appended again is a second retained note: the first
            // row keeps its own id, text, author and instant.
            using HttpResponseMessage second = await client.PostAsync(
                url, NoteBody("Retained E07 verification note."));
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

            Guid secondNoteId;
            using (JsonDocument body = JsonDocument.Parse(await second.Content.ReadAsStringAsync()))
            {
                secondNoteId = body.RootElement.GetProperty("noteId").GetGuid();
            }

            Assert.NotEqual(noteId, secondNoteId);

            using HttpResponseMessage appended = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, appended.StatusCode);
            using (JsonDocument page = JsonDocument.Parse(await appended.Content.ReadAsStringAsync()))
            {
                Assert.Equal(2, page.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement[] items = [.. page.RootElement.GetProperty("items").EnumerateArray()];
                Assert.Equal(2, items.Length);
                Assert.Equal(secondNoteId.ToString("D"), items[0].GetProperty("noteId").GetString());

                JsonElement retained = items[1];
                Assert.Equal(noteId.ToString("D"), retained.GetProperty("noteId").GetString());
                Assert.Equal("Retained E07 verification note.", retained.GetProperty("text").GetString());
                Assert.Equal(admin.Id.Value.ToString("D"), retained.GetProperty("authorUserId").GetString());
                Assert.Equal(createdOnUtc, retained.GetProperty("createdOnUtc").GetDateTime());
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n ")]
    public async Task Post_EmptyOrWhitespaceText_Is400WithTheInvalidTextCodeAndWritesNothing(
        string? text)
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(AccountType.SuperAdmin, 61)));

            using HttpResponseMessage response = await client.PostAsync(
                NotesUrl(caregiver.Id.Value), NoteBody(text));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using JsonDocument problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(
                "Caregivers.ReviewNote.InvalidText",
                problem.RootElement.GetProperty("code").GetString());
            Assert.Empty(await db.ReviewNotes.ToListAsync());
        }
    }

    [Fact]
    public async Task Post_TextOverTheBoundIs400AndTheExactBoundIsAccepted()
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(AccountType.SuperAdmin, 62)));
            string url = NotesUrl(caregiver.Id.Value);

            using HttpResponseMessage overLength = await client.PostAsync(
                url, NoteBody(new string('x', CaregiverReviewNote.MaximumTextLength + 1)));
            Assert.Equal(HttpStatusCode.BadRequest, overLength.StatusCode);
            using (JsonDocument problem = JsonDocument.Parse(await overLength.Content.ReadAsStringAsync()))
            {
                Assert.Equal(
                    "Caregivers.ReviewNote.InvalidText",
                    problem.RootElement.GetProperty("code").GetString());
            }

            Assert.Empty(await db.ReviewNotes.ToListAsync());

            using HttpResponseMessage atBound = await client.PostAsync(
                url, NoteBody(new string('x', CaregiverReviewNote.MaximumTextLength)));
            Assert.Equal(HttpStatusCode.Created, atBound.StatusCode);

            using HttpResponseMessage listed = await client.GetAsync(url);
            using JsonDocument page = JsonDocument.Parse(await listed.Content.ReadAsStringAsync());
            JsonElement item = Assert.Single(page.RootElement.GetProperty("items").EnumerateArray());
            Assert.Equal(
                CaregiverReviewNote.MaximumTextLength,
                item.GetProperty("text").GetString()!.Length);
        }
    }

    [Fact]
    public async Task BothRoutes_UnknownOrEmptyCaregiver_Are404WithTheCaregiverCodeAndATraceId()
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            await AddCaregiverAsync(db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(AccountType.SuperAdmin, 71)));
            string unknown = NotesUrl(Guid.NewGuid());
            string empty = NotesUrl(Guid.Empty);

            using HttpResponseMessage listedUnknown = await client.GetAsync(unknown);
            Assert.Equal(HttpStatusCode.NotFound, listedUnknown.StatusCode);
            Assert.Equal("application/problem+json", listedUnknown.Content.Headers.ContentType?.MediaType);
            using (JsonDocument problem = JsonDocument.Parse(
                await listedUnknown.Content.ReadAsStringAsync()))
            {
                Assert.Equal(
                    "Caregivers.ReviewNote.CaregiverNotFound",
                    problem.RootElement.GetProperty("code").GetString());
                Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
                Assert.Equal("Not Found", problem.RootElement.GetProperty("title").GetString());
                Assert.False(string.IsNullOrWhiteSpace(
                    problem.RootElement.GetProperty("traceId").GetString()));
            }

            // An unknown caregiver is reported before the text shape, so a blank
            // text never masks a missing caregiver.
            using HttpResponseMessage addedUnknown = await client.PostAsync(unknown, NoteBody("   "));
            Assert.Equal(HttpStatusCode.NotFound, addedUnknown.StatusCode);
            using (JsonDocument problem = JsonDocument.Parse(
                await addedUnknown.Content.ReadAsStringAsync()))
            {
                Assert.Equal(
                    "Caregivers.ReviewNote.CaregiverNotFound",
                    problem.RootElement.GetProperty("code").GetString());
            }

            using HttpResponseMessage listedEmpty = await client.GetAsync(empty);
            using HttpResponseMessage addedEmpty = await client.PostAsync(empty, NoteBody("note"));
            Assert.Equal(HttpStatusCode.NotFound, listedEmpty.StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, addedEmpty.StatusCode);

            Assert.Empty(await db.ReviewNotes.ToListAsync());
        }
    }

    [Fact]
    public async Task Get_PagesNewestFirstAndClampsPagingInsteadOfRejectingIt()
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            UserId author = UserId.New();
            DateTime now = new(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc);

            for (int index = 0; index < 5; index++)
            {
                db.ReviewNotes.Add(CaregiverReviewNote.Write(
                    caregiver.Id, author, $"retained note {index}", now.AddMinutes(index)));
            }

            await db.SaveChangesAsync();

            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(AccountType.SupportAdmin, 72)));
            string url = NotesUrl(caregiver.Id.Value);

            using HttpResponseMessage firstPage = await client.GetAsync($"{url}?page=1&pageSize=2");
            Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
            using (JsonDocument page = JsonDocument.Parse(await firstPage.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1, page.RootElement.GetProperty("page").GetInt32());
                Assert.Equal(2, page.RootElement.GetProperty("pageSize").GetInt32());
                Assert.Equal(5, page.RootElement.GetProperty("totalCount").GetInt32());
                JsonElement[] items = [.. page.RootElement.GetProperty("items").EnumerateArray()];
                Assert.Equal(2, items.Length);
                Assert.Equal("retained note 4", items[0].GetProperty("text").GetString());
                Assert.Equal("retained note 3", items[1].GetProperty("text").GetString());
            }

            using HttpResponseMessage secondPage = await client.GetAsync($"{url}?page=2&pageSize=2");
            using (JsonDocument page = JsonDocument.Parse(await secondPage.Content.ReadAsStringAsync()))
            {
                Assert.Equal(5, page.RootElement.GetProperty("totalCount").GetInt32());
                Assert.Equal(2, page.RootElement.GetProperty("items").GetArrayLength());
                Assert.Equal(
                    "retained note 2",
                    page.RootElement.GetProperty("items")[0].GetProperty("text").GetString());
            }

            using HttpResponseMessage beyondPage = await client.GetAsync($"{url}?page=9&pageSize=2");
            using (JsonDocument page = JsonDocument.Parse(await beyondPage.Content.ReadAsStringAsync()))
            {
                Assert.Equal(9, page.RootElement.GetProperty("page").GetInt32());
                Assert.Equal(5, page.RootElement.GetProperty("totalCount").GetInt32());
                Assert.Empty(page.RootElement.GetProperty("items").EnumerateArray());
            }

            using HttpResponseMessage clamped = await client.GetAsync($"{url}?page=0&pageSize=0");
            using (JsonDocument page = JsonDocument.Parse(await clamped.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1, page.RootElement.GetProperty("page").GetInt32());
                Assert.Equal(20, page.RootElement.GetProperty("pageSize").GetInt32());
                Assert.Equal(5, page.RootElement.GetProperty("totalCount").GetInt32());
            }

            using HttpResponseMessage capped = await client.GetAsync($"{url}?page=1&pageSize=1000");
            using (JsonDocument page = JsonDocument.Parse(await capped.Content.ReadAsStringAsync()))
            {
                Assert.Equal(100, page.RootElement.GetProperty("pageSize").GetInt32());
                Assert.Equal(5, page.RootElement.GetProperty("totalCount").GetInt32());
            }

            using HttpResponseMessage negative = await client.GetAsync($"{url}?page=-3&pageSize=-1");
            using (JsonDocument page = JsonDocument.Parse(await negative.Content.ReadAsStringAsync()))
            {
                Assert.Equal(1, page.RootElement.GetProperty("page").GetInt32());
                Assert.Equal(20, page.RootElement.GetProperty("pageSize").GetInt32());
            }
        }
    }

    [Fact]
    public async Task Get_OneCaregiversNotesAreNeverVisibleOnAnotherCaregiver()
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            Caregiver otherCaregiver = await AddCaregiverAsync(db);
            db.ReviewNotes.Add(CaregiverReviewNote.Write(
                otherCaregiver.Id,
                UserId.New(),
                "another caregiver's administrative note",
                new DateTime(2026, 10, 21, 9, 0, 0, DateTimeKind.Utc)));
            await db.SaveChangesAsync();

            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(AccountType.SuperAdmin, 73)));

            using HttpResponseMessage empty = await client.GetAsync(NotesUrl(caregiver.Id.Value));
            Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
            using (JsonDocument page = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()))
            {
                Assert.Empty(page.RootElement.GetProperty("items").EnumerateArray());
                Assert.Equal(0, page.RootElement.GetProperty("totalCount").GetInt32());
            }

            using HttpResponseMessage own = await client.GetAsync(NotesUrl(otherCaregiver.Id.Value));
            using JsonDocument ownPage = JsonDocument.Parse(await own.Content.ReadAsStringAsync());
            Assert.Equal(1, ownPage.RootElement.GetProperty("totalCount").GetInt32());
            Assert.Equal(
                "another caregiver's administrative note",
                Assert.Single(ownPage.RootElement.GetProperty("items").EnumerateArray())
                    .GetProperty("text")
                    .GetString());
        }
    }

    [Theory]
    [InlineData("{\"text\": 123}")]
    [InlineData("not json at all")]
    [InlineData("{}")]
    public async Task Post_AMalformedOrEmptyBody_IsAClientErrorAndNeverAServerError(string body)
    {
        (WebApplicationFactory<Program> factory, CaregiversDbContext db) = CreateNotesHost();
        using (factory)
        using (db)
        {
            Caregiver caregiver = await AddCaregiverAsync(db);
            using HttpClient client = CreateClient(factory);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Bearer", CreateToken(CreateUser(AccountType.SuperAdmin, 74)));

            using HttpResponseMessage response = await client.PostAsync(
                NotesUrl(caregiver.Id.Value),
                new StringContent(body, Encoding.UTF8, "application/json"));

            Assert.True(
                (int)response.StatusCode is >= 400 and < 500,
                $"a malformed body must never produce a server error, got {(int)response.StatusCode}");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Empty(await db.ReviewNotes.ToListAsync());
        }
    }

    private (WebApplicationFactory<Program> Factory, CaregiversDbContext Db) CreateNotesHost()
    {
        CaregiversDbContext db = new(new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase($"e07-review-notes-{Guid.NewGuid():N}")
            .Options);

        WebApplicationFactory<Program> factory = _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<CaregiversDbContext>();
                services.RemoveAll<ICaregiversDbContext>();
                services.AddSingleton(db);
                services.AddSingleton<ICaregiversDbContext>(db);
            }));

        return (factory, db);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    private static StringContent NoteBody(string? text) =>
        new(JsonSerializer.Serialize(new { text }), Encoding.UTF8, "application/json");

    private static string NotesUrl(Guid caregiverId) =>
        $"/api/v1/admin/caregivers/{caregiverId:D}/notes";

    private static async Task<Caregiver> AddCaregiverAsync(CaregiversDbContext db)
    {
        Caregiver caregiver = Caregiver.Create(UserId.New(), CaregiverType.Companion);
        db.Caregivers.Add(caregiver);
        await db.SaveChangesAsync();
        return caregiver;
    }

    private string CreateToken(User user)
    {
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }

    private static User CreateUser(AccountType accountType, int index)
    {
        User user = User.Create(
            FullName.Create($"E07 notes user {index} (arabic name slot)"),
            FullName.Create($"E07 Notes User {index}"),
            Email.Create($"e07.notes.user{index}@example.test"),
            PhoneNumber.Create($"+20100000{index:D4}"));
        user.AddAccount(accountType);
        return user;
    }

    private static void AssertActionTemplate(string action, string template, string verb)
    {
        HttpMethodAttribute attribute = typeof(AdminCaregiversController)
            .GetMethod(action)!
            .GetCustomAttributes<HttpMethodAttribute>()
            .Single();

        Assert.Equal(template, attribute.Template);
        Assert.Equal(verb, Assert.Single(attribute.HttpMethods));
    }

    private static int[] PublishedStatusCodes(string action) =>
    [
        .. typeof(AdminCaregiversController)
            .GetMethod(action)!
            .GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Select(attribute => attribute.StatusCode)
            .OrderBy(status => status)
    ];

    private static void AssertExactJsonProperties(JsonElement root, params string[] names) =>
        Assert.Equal<string>(
            [.. names.OrderBy(name => name, StringComparer.Ordinal)],
            [.. root.EnumerateObject().Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)]);

    private static void AssertExactProperties(JsonElement schema, params string[] names) =>
        AssertExactJsonProperties(schema, names);

    private JsonElement Document()
    {
        if (_document is null)
        {
            using HttpClient client = _host.CreatePortalClient();
            using HttpResponseMessage response = client.GetAsync("/openapi/v1.json").GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            _document = JsonDocument.Parse(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
        }

        return _document.RootElement;
    }

    private static string[] PathMethods(JsonElement root, string path)
    {
        Assert.True(
            root.GetProperty("paths").TryGetProperty(path, out JsonElement pathItem),
            $"Missing path {path}.");
        return
        [
            .. pathItem.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => HttpVerbs.Contains(name, StringComparer.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
        ];
    }

    private static JsonElement GetOperation(JsonElement root, string path, string method)
    {
        Assert.True(
            root.GetProperty("paths").TryGetProperty(path, out JsonElement pathItem),
            $"Missing path {path}.");
        Assert.True(
            pathItem.TryGetProperty(method, out JsonElement operation),
            $"Missing {method} on {path}.");
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

    private static CultureInfo InvariantCulture => CultureInfo.InvariantCulture;
}
