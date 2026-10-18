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

        (LegalController controller, CancellationToken token, LegalDocumentType expectedType)[] cases =
        [
            (CreateLegalController(sender, AccountType.CareHomeOwner), CancellationToken.None, LegalDocumentType.PrivacyPolicy),
            (CreateLegalController(sender, AccountType.CareHomeOwner), CancellationToken.None, LegalDocumentType.TermsAndConditions)
        ];

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
    public async Task HelpCenter_DoesNotSupportOwnerAudience()
    {
        Assert.False(LegalAudience.CareHomeOwner.IsHelpAudience());
        Assert.Throws<DomainException>(() =>
            HelpFaq.Create(
                LegalAudience.CareHomeOwner,
                "س",
                "Q",
                "ج",
                "A",
                0,
                true));

        GetHelpCenterQueryHandler handler = new(CreateDbContext());
        Result<HelpCenterResponse> result =
            await handler.Handle(
                new GetHelpCenterQuery(LegalAudience.CareHomeOwner),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(ContentErrors.UnsupportedAudience.Code, result.Error.Code);
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
        AccountType accountType)
    {
        return new LegalController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(
                        new ClaimsIdentity(
                        [
                            new Claim(
                                AuthClaimNames.AccountType,
                                accountType.ToString())
                        ],
                        "test"))
                }
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
        public async Task LegalAudience_OpenApi_IncludesCareHomeOwnerFiveAndPreservesExisting()
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
                "/api/v1/legal/terms"
            })
            {
                JsonElement operation =
                    root.GetProperty("paths")
                        .GetProperty(path)
                        .GetProperty("get");

                bool hasAudienceParameter = false;
                if (operation.TryGetProperty("parameters", out JsonElement parameters))
                {
                    hasAudienceParameter = parameters.EnumerateArray().Any(parameter =>
                        parameter.TryGetProperty("name", out JsonElement name) &&
                        string.Equals(
                            name.GetString(),
                            "audience",
                            StringComparison.OrdinalIgnoreCase));
                }

                Assert.False(
                    hasAudienceParameter,
                    $"Legal route {path} must not expose an audience parameter.");
            }
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
