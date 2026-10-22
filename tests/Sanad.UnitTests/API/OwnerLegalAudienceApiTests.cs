using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace Sanad.UnitTests.API;

public sealed class OwnerLegalAudienceApiTests
{
    [Fact]
    public void LegalAudience_CareHomeOwner_HasNumericValueFive()
    {
        Assert.Equal(5, (int)LegalAudience.CareHomeOwner);
        Assert.True(LegalAudience.CareHomeOwner.IsDefined());
    }

    [Fact]
    public async Task OwnerClaim_MapsToBothLegalRoutes()
    {
        GetPublishedLegalDocumentQueryHandler handler = new(CreateDbContext());
        ForwardingSender sender = new(handler);

        // Invoke through the matching route method for each expected type.
        (LegalController controller, CancellationToken token, LegalDocumentType expectedType, Func<LegalController, CancellationToken, Task<IActionResult>> invoke)[] routes =
        [
            (CreateLegalController(sender, AccountType.CareHomeOwner), CancellationToken.None, LegalDocumentType.PrivacyPolicy, (c, t) => c.GetPrivacyPolicy(t)),
            (CreateLegalController(sender, AccountType.CareHomeOwner), CancellationToken.None, LegalDocumentType.TermsAndConditions, (c, t) => c.GetTerms(t))
        ];

        foreach (var route in routes)
        {
            sender.LastRequest = null;

            await route.invoke(route.controller, route.token);

            GetPublishedLegalDocumentQuery query =
                Assert.IsType<GetPublishedLegalDocumentQuery>(sender.LastRequest);
            Assert.Equal(LegalAudience.CareHomeOwner, query.Audience);
            Assert.Equal(route.expectedType, query.DocumentType);
        }
    }

    [Fact]
    public async Task OwnerReads_AreIndependentlyPublished()
    {
        using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);

        var privacyCreate = await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.CareHomeOwner,
                [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 2)
                ]),
            CancellationToken.None);
        Assert.True(privacyCreate.IsSuccess, privacyCreate.Error?.Code);
        LegalDocumentId privacyId = privacyCreate.Value.Id;
        await publishHandler.Handle(new PublishLegalDocumentCommand(privacyId), CancellationToken.None);

        LegalDocumentId termsId = (await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.TermsAndConditions,
                LegalAudience.CareHomeOwner,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None)).Value.Id;
        await publishHandler.Handle(new PublishLegalDocumentCommand(termsId), CancellationToken.None);

        GetPublishedLegalDocumentQueryHandler queryHandler = new(dbContext);

        Result<LegalDocumentResponse> privacy =
            await queryHandler.Handle(
                new GetPublishedLegalDocumentQuery(
                    LegalDocumentType.PrivacyPolicy,
                    LegalAudience.CareHomeOwner),
                CancellationToken.None);
        Result<LegalDocumentResponse> terms =
            await queryHandler.Handle(
                new GetPublishedLegalDocumentQuery(
                    LegalDocumentType.TermsAndConditions,
                    LegalAudience.CareHomeOwner),
                CancellationToken.None);

        Assert.True(privacy.IsSuccess);
        Assert.True(terms.IsSuccess);
        Assert.Equal(LegalDocumentType.PrivacyPolicy, privacy.Value.DocumentType);
        Assert.Equal(LegalDocumentType.TermsAndConditions, terms.Value.DocumentType);
        Assert.Equal(LegalAudience.CareHomeOwner, privacy.Value.Audience);
        Assert.Equal(LegalAudience.CareHomeOwner, terms.Value.Audience);
    }

    [Fact]
    public async Task OwnerMissingPublication_ReturnsNotPublished_DespiteOtherAudience()
    {
        using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);

        LegalDocumentId familyPrivacy = (await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.Family,
                [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 2)
                ]),
            CancellationToken.None)).Value.Id;
        await publishHandler.Handle(new PublishLegalDocumentCommand(familyPrivacy), CancellationToken.None);

        GetPublishedLegalDocumentQueryHandler queryHandler = new(dbContext);
        Result<LegalDocumentResponse> result =
            await queryHandler.Handle(
                new GetPublishedLegalDocumentQuery(
                    LegalDocumentType.PrivacyPolicy,
                    LegalAudience.CareHomeOwner),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(LegalErrors.NotPublished.Code, result.Error.Code);

        ForwardingSender controllerSender = new(queryHandler);
        LegalController controller = CreateLegalController(
            controllerSender,
            AccountType.CareHomeOwner);

        IActionResult controllerResult =
            await controller.GetPrivacyPolicy(CancellationToken.None);

        ObjectResult notFound =
            Assert.IsType<ObjectResult>(controllerResult);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        ProblemDetails problem =
            Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(
            LegalErrors.NotPublished.Code,
            problem.Extensions["code"]);
    }

    [Fact]
    public async Task HelpCenter_SupportsOwnerAudience()
    {
        Assert.True(LegalAudience.CareHomeOwner.IsHelpAudience());

        GetHelpCenterQueryHandler handler = new(CreateDbContext());
        Result<HelpCenterResponse> result =
            await handler.Handle(
                new GetHelpCenterQuery(LegalAudience.CareHomeOwner),
                CancellationToken.None);

        // Owner FAQs are served; empty store yields success with no items.
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ExistingAudiences_RemainDefined()
    {
        foreach (LegalAudience audience in new[]
        {
            LegalAudience.Family,
            LegalAudience.MedicalCaregiver,
            LegalAudience.CompanionCaregiver,
            LegalAudience.Elderly
        })
        {
            Assert.True(audience.IsDefined());
            Assert.True(audience.IsHelpAudience());
        }
    }

    [Fact]
    public async Task SingleAccount_OmittedAudience_AutoSelectsOwnedAudience()
    {
        using CmsDbContext dbContext = CreateDbContext();
        await PublishOwnerPrivacyAsync(dbContext);
        RoutingSender sender = CreateRoutingSender(dbContext);
        LegalController controller = CreateLegalController(
            sender,
            AccountType.CareHomeOwner);

        IActionResult result = await controller.GetPrivacyPolicy(
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        LegalDocumentResponse document =
            Assert.IsType<LegalDocumentResponse>(ok.Value);
        Assert.Equal(LegalAudience.CareHomeOwner, document.Audience);
    }

    [Fact]
    public async Task MultiAccount_ExplicitOwnerAudience_WinsOverClaimOrder()
    {
        using CmsDbContext dbContext = CreateDbContext();
        await PublishOwnerPrivacyAsync(dbContext);
        RoutingSender sender = CreateRoutingSender(dbContext);

        foreach (AccountType[] accountTypes in new[]
        {
            new[]
            {
                AccountType.Family,
                AccountType.CareHomeOwner
            },
            new[]
            {
                AccountType.CareHomeOwner,
                AccountType.Family
            }
        })
        {
            sender.LastRequest = null;
            LegalController controller = CreateLegalController(
                sender,
                accountTypes);

            IActionResult result = await controller.GetPrivacyPolicy(
                CancellationToken.None,
                ((int)LegalAudience.CareHomeOwner).ToString());

            var ok = Assert.IsType<OkObjectResult>(result);
            LegalDocumentResponse document =
                Assert.IsType<LegalDocumentResponse>(ok.Value);
            Assert.Equal(LegalAudience.CareHomeOwner, document.Audience);

            GetPublishedLegalDocumentQuery sent =
                Assert.IsType<GetPublishedLegalDocumentQuery>(
                    sender.LastRequest);
            Assert.Equal(LegalAudience.CareHomeOwner, sent.Audience);
        }
    }

    [Fact]
    public async Task MultiAccount_OmittedAudience_RequiresExplicitSelection()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        LegalController controller = CreateLegalController(
            sender,
            AccountType.Family,
            AccountType.CareHomeOwner);

        IActionResult result = await controller.GetPrivacyPolicy(
            CancellationToken.None);

        AssertProblem(
            result,
            StatusCodes.Status400BadRequest,
            "Cms.Content.AudienceRequired");
        Assert.Null(sender.LastRequest);
    }

    [Theory]
    [InlineData("9")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("1,2")]
    public async Task MalformedAudience_IsCodedBadRequest(
        string audience)
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        LegalController controller = CreateLegalController(
            sender,
            AccountType.Family,
            AccountType.CareHomeOwner);

        IActionResult result = await controller.GetPrivacyPolicy(
            CancellationToken.None,
            audience);

        AssertProblem(
            result,
            StatusCodes.Status400BadRequest,
            "Cms.Content.InvalidAudience");
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task ValidButUnownedAudience_IsForbidden()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        LegalController controller = CreateLegalController(
            sender,
            AccountType.Family);

        IActionResult result = await controller.GetPrivacyPolicy(
            CancellationToken.None,
            ((int)LegalAudience.CareHomeOwner).ToString());

        AssertProblem(
            result,
            StatusCodes.Status403Forbidden,
            ContentErrors.UnsupportedAudience.Code);
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task HelpCenter_ExplicitOwnerAudience_WithDuplicateClaims_ResolvesOnce()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        HelpCenterController controller = CreateHelpCenterController(
            sender,
            AccountType.CareHomeOwner,
            AccountType.CareHomeOwner,
            AccountType.Family);

        IActionResult result = await controller.GetHelpCenter(
            CancellationToken.None,
            ((int)LegalAudience.CareHomeOwner).ToString());

        var ok = Assert.IsType<OkObjectResult>(result);
        HelpCenterResponse response =
            Assert.IsType<HelpCenterResponse>(ok.Value);
        Assert.Empty(response.Faqs);

        GetHelpCenterQuery sent =
            Assert.IsType<GetHelpCenterQuery>(sender.LastRequest);
        Assert.Equal(LegalAudience.CareHomeOwner, sent.Audience);
    }

    [Fact]
    public async Task HelpCenter_OmittedSingle_AutoSelects_AndDuplicateClaimsCollapse()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        HelpCenterController controller = CreateHelpCenterController(
            sender,
            AccountType.Family,
            AccountType.Family);

        IActionResult result = await controller.GetHelpCenter(
            CancellationToken.None);

        // Duplicate Family claims collapse to one owned audience, so an
        // omitted audience still auto-selects instead of requiring one.
        Assert.IsType<OkObjectResult>(result);
        GetHelpCenterQuery sent =
            Assert.IsType<GetHelpCenterQuery>(sender.LastRequest);
        Assert.Equal(LegalAudience.Family, sent.Audience);
    }

    [Fact]
    public async Task HelpCenter_OmittedMultiple_RequiresExplicitSelection()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        HelpCenterController controller = CreateHelpCenterController(
            sender,
            AccountType.CareHomeOwner,
            AccountType.Family);

        IActionResult result = await controller.GetHelpCenter(
            CancellationToken.None);

        AssertProblem(
            result,
            StatusCodes.Status400BadRequest,
            "Cms.Content.AudienceRequired");
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task HelpCenter_MalformedAndUnowned_DenyLikeLegal()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        HelpCenterController controller = CreateHelpCenterController(
            sender,
            AccountType.Family);

        AssertProblem(
            await controller.GetHelpCenter(
                CancellationToken.None,
                "abc"),
            StatusCodes.Status400BadRequest,
            "Cms.Content.InvalidAudience");

        AssertProblem(
            await controller.GetHelpCenter(
                CancellationToken.None,
                ((int)LegalAudience.CareHomeOwner).ToString()),
            StatusCodes.Status403Forbidden,
            ContentErrors.UnsupportedAudience.Code);

        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task IrrelevantAdminClaim_IsIgnoredWhenAnAppAudienceIsOwned()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        HelpCenterController controller = CreateHelpCenterController(
            sender,
            AccountType.SupportAdmin,
            AccountType.Family);

        IActionResult result = await controller.GetHelpCenter(
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        GetHelpCenterQuery sent =
            Assert.IsType<GetHelpCenterQuery>(sender.LastRequest);
        Assert.Equal(LegalAudience.Family, sent.Audience);
    }

    [Fact]
    public async Task AdminRole_BothRoutes_KeepDocumentedDenial()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        LegalController legalController = CreateLegalController(
            sender,
            AccountType.SuperAdmin);
        HelpCenterController helpController = CreateHelpCenterController(
            sender,
            AccountType.SuperAdmin);

        AssertProblem(
            await legalController.GetPrivacyPolicy(
                CancellationToken.None),
            StatusCodes.Status403Forbidden,
            ContentErrors.UnsupportedAudience.Code);

        AssertProblem(
            await helpController.GetHelpCenter(
                CancellationToken.None),
            StatusCodes.Status403Forbidden,
            ContentErrors.UnsupportedAudience.Code);

        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task AdminClaim_MalformedAudience_IsCodedBadRequestOnBothRoutes()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        LegalController legalController = CreateLegalController(
            sender,
            AccountType.SuperAdmin);
        HelpCenterController helpController = CreateHelpCenterController(
            sender,
            AccountType.SuperAdmin);

        // The malformed check runs before ownership, so an admin token that
        // sends a malformed audience gets 400, not the admin 403.
        AssertProblem(
            await legalController.GetPrivacyPolicy(
                CancellationToken.None,
                "abc"),
            StatusCodes.Status400BadRequest,
            "Cms.Content.InvalidAudience");

        AssertProblem(
            await helpController.GetHelpCenter(
                CancellationToken.None,
                "abc"),
            StatusCodes.Status400BadRequest,
            "Cms.Content.InvalidAudience");

        Assert.Null(sender.LastRequest);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task EmptyAudience_IsTreatedAsOmitted_OnBothRoutes(
        string audience)
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        LegalController legalController = CreateLegalController(
            sender,
            AccountType.CareHomeOwner,
            AccountType.Family);
        HelpCenterController helpController = CreateHelpCenterController(
            sender,
            AccountType.CareHomeOwner,
            AccountType.Family);

        // Empty or whitespace behaves exactly like an omitted parameter, so a
        // token that owns two audiences must still name one.
        AssertProblem(
            await legalController.GetPrivacyPolicy(
                CancellationToken.None,
                audience),
            StatusCodes.Status400BadRequest,
            "Cms.Content.AudienceRequired");

        AssertProblem(
            await helpController.GetHelpCenter(
                CancellationToken.None,
                audience),
            StatusCodes.Status400BadRequest,
            "Cms.Content.AudienceRequired");

        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task HelpCenter_OmittedSingle_OwnerClaim_IsServed()
    {
        using CmsDbContext dbContext = CreateDbContext();
        RoutingSender sender = CreateRoutingSender(dbContext);
        HelpCenterController controller = CreateHelpCenterController(
            sender,
            AccountType.CareHomeOwner);

        // Care Home Owner is an approved Help Center audience (F5, domain
        // IsHelpAudience), so a single-owned-audience owner token is served
        // 200 on the same terms as the legal routes instead of being denied.
        IActionResult result = await controller.GetHelpCenter(
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        GetHelpCenterQuery sent =
            Assert.IsType<GetHelpCenterQuery>(sender.LastRequest);
        Assert.Equal(LegalAudience.CareHomeOwner, sent.Audience);
    }

    private static void AssertProblem(
        IActionResult result,
        int expectedStatus,
        string expectedCode)
    {
        ObjectResult objectResult =
            Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);

        ProblemDetails problem =
            Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(
            expectedCode,
            problem.Extensions["code"]);
    }

    private static RoutingSender CreateRoutingSender(
        CmsDbContext dbContext)
    {
        return new RoutingSender(
            new GetPublishedLegalDocumentQueryHandler(dbContext),
            new GetHelpCenterQueryHandler(dbContext));
    }

    private static async Task PublishOwnerPrivacyAsync(
        CmsDbContext dbContext)
    {
        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);

        LegalDocumentId ownerPrivacy = (await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.CareHomeOwner,
                [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 2)
                ]),
            CancellationToken.None)).Value.Id;

        await publishHandler.Handle(
            new PublishLegalDocumentCommand(ownerPrivacy),
            CancellationToken.None);
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(options);
    }

    private static LegalController CreateLegalController(
        ISender sender,
        params AccountType[] accountTypes)
    {
        return new LegalController(sender)
        {
            ControllerContext = CreateContext(accountTypes)
        };
    }

    private static HelpCenterController CreateHelpCenterController(
        ISender sender,
        params AccountType[] accountTypes)
    {
        return new HelpCenterController(sender)
        {
            ControllerContext = CreateContext(accountTypes)
        };
    }

    private static ControllerContext CreateContext(
        IReadOnlyList<AccountType> accountTypes)
    {
        return new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        accountTypes.Select(
                            accountType =>
                                new Claim(
                                    AuthClaimNames.AccountType,
                                    accountType.ToString())),
                        "test"))
            }
        };
    }

    private static LegalSectionInput Section(
        LegalSectionType sectionType,
        int displayOrder)
    {
        return new LegalSectionInput(
            sectionType,
            displayOrder,
            "عنوان",
            "Title",
            "وصف",
            "Description",
            sectionType == LegalSectionType.UserRights ? ["حق"] : [],
            sectionType == LegalSectionType.UserRights ? ["Right"] : []);
    }

    public sealed class OwnerLegalOpenApiTests :
        IClassFixture<PortalApiMetadataHost>
    {
        private readonly PortalApiMetadataHost _host;

        public OwnerLegalOpenApiTests(
            PortalApiMetadataHost host)
        {
            _host = host;
        }

        [Fact]
        public async Task LegalAudience_OpenApi_IncludesCareHomeOwnerFiveAndOptionalAudienceParameter()
        {
            using HttpClient client = _host.CreatePortalClient();
            using HttpResponseMessage response =
                await client.GetAsync("/openapi/v1.json");
            response.EnsureSuccessStatusCode();

            using JsonDocument document =
                JsonDocument.Parse(
                    await response.Content.ReadAsStringAsync());
            JsonElement root = document.RootElement;

            JsonElement schema =
                root.GetProperty("components")
                    .GetProperty("schemas")
                    .GetProperty("LegalAudience");

            int[] values =
                schema.GetProperty("enum")
                    .EnumerateArray()
                    .Select(value => value.GetInt32())
                    .ToArray();
            Assert.Equal(new[] { 1, 2, 3, 4, 5 }, values);

            string[] names =
                schema.GetProperty("x-enumNames")
                    .EnumerateArray()
                    .Select(value => value.GetString()!)
                    .ToArray();
            Assert.Contains("CareHomeOwner", names);

            foreach (string path in new[]
            {
                "/api/v1/legal/privacy-policy",
                "/api/v1/legal/terms",
                "/api/v1/help-center"
            })
            {
                JsonElement operation =
                    root.GetProperty("paths")
                        .GetProperty(path)
                        .GetProperty("get");

                Assert.True(
                    operation.TryGetProperty("parameters", out JsonElement parameters),
                    $"No parameters for {path}.");

                // T3: legal and Help Center both accept the optional
                // audience query parameter deterministically.
                JsonElement audienceParameter = parameters
                    .EnumerateArray()
                    .Single(parameter =>
                        string.Equals(
                            parameter.GetProperty("name").GetString(),
                            "audience",
                            StringComparison.OrdinalIgnoreCase));

                Assert.Equal(
                    "query",
                    audienceParameter.GetProperty("in").GetString());

                if (audienceParameter.TryGetProperty(
                        "required",
                        out JsonElement required))
                {
                    Assert.False(
                        required.GetBoolean(),
                        $"audience must stay optional on {path}.");
                }
            }
        }
    }

    private sealed class RoutingSender : ISender
    {
        private readonly GetPublishedLegalDocumentQueryHandler _legalHandler;

        private readonly GetHelpCenterQueryHandler _helpHandler;

        public RoutingSender(
            GetPublishedLegalDocumentQueryHandler legalHandler,
            GetHelpCenterQueryHandler helpHandler)
        {
            _legalHandler = legalHandler;
            _helpHandler = helpHandler;
        }

        public object? LastRequest { get; set; }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (request is GetPublishedLegalDocumentQuery legalQuery)
            {
                return (TResponse)(object)await _legalHandler.Handle(
                    legalQuery,
                    cancellationToken);
            }

            if (request is GetHelpCenterQuery helpQuery)
            {
                return (TResponse)(object)await _helpHandler.Handle(
                    helpQuery,
                    cancellationToken);
            }

            throw new NotSupportedException();
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException();
        }

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class ForwardingSender : ISender
    {
        private readonly GetPublishedLegalDocumentQueryHandler _handler;

        public ForwardingSender(
            GetPublishedLegalDocumentQueryHandler handler)
        {
            _handler = handler;
        }

        public object? LastRequest { get; set; }

        public async Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            if (request is GetPublishedLegalDocumentQuery query)
            {
                Result<LegalDocumentResponse> result =
                    await _handler.Handle(
                        query,
                        cancellationToken);

                return (TResponse)(object)result;
            }

            throw new NotSupportedException();
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest
        {
            throw new NotSupportedException();
        }

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }
}
