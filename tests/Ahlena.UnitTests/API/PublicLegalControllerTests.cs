using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Help;
using Ahlena.Modules.Cms.Application.Legal;
using Ahlena.Modules.Cms.Domain.Legal;
using Ahlena.Modules.Cms.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Authentication.Tokens;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// T9-B public legal (decision 6A): the anonymous
/// <c>GET /api/v1/public/legal/{documentType}</c> read, the Public audience
/// semantics, and the guarantee that the authenticated app reads are
/// unchanged and never serve Public.
/// </summary>
public sealed class PublicLegalControllerTests
{
    [Fact]
    public void PublicAudience_IsZeroDefined_AndHelpAudienceExcludesIt()
    {
        Assert.Equal(0, (int)LegalAudience.Public);
        Assert.True(LegalAudience.Public.IsDefined());

        // Help Center stays authenticated: Public is not a help audience.
        Assert.False(LegalAudience.Public.IsHelpAudience());

        // The five app audiences keep their original numbers, order and
        // semantics (the column stores the enum as int, so no row changes).
        (LegalAudience Audience, int Value)[] appAudiences =
        [
            (LegalAudience.Family, 1),
            (LegalAudience.MedicalCaregiver, 2),
            (LegalAudience.CompanionCaregiver, 3),
            (LegalAudience.Elderly, 4),
            (LegalAudience.CareHomeOwner, 5)
        ];

        foreach ((LegalAudience audience, int value) in appAudiences)
        {
            Assert.Equal(value, (int)audience);
            Assert.True(audience.IsDefined());
            Assert.True(audience.IsHelpAudience());
        }
    }

    [Fact]
    public async Task HelpCenterHandler_RejectsThePublicAudience()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        GetHelpCenterQueryHandler handler = new(dbContext);

        Result<HelpCenterResponse> result =
            await handler.Handle(
                new GetHelpCenterQuery(LegalAudience.Public),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            ContentErrors.UnsupportedAudience.Code,
            result.Error.Code);
    }

    [Fact]
    public async Task PublishedQueryHandler_ReturnsLatestPublishedPublicVersion()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        // A different audience's published version is seeded first to prove
        // there is no cross-audience fallback.
        await PublishAsync(
            dbContext,
            LegalDocumentType.PrivacyPolicy,
            LegalAudience.Family);

        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);

        LegalDocumentId first = (await createHandler.Handle(
            PublicPrivacyCommand(),
            CancellationToken.None)).Value.Id;
        await publishHandler.Handle(
            new PublishLegalDocumentCommand(first),
            CancellationToken.None);

        // A second version of the same pair becomes the latest publication;
        // the first is archived and must not be returned.
        LegalDocumentId second = (await createHandler.Handle(
            PublicPrivacyCommand(),
            CancellationToken.None)).Value.Id;
        await publishHandler.Handle(
            new PublishLegalDocumentCommand(second),
            CancellationToken.None);

        GetPublishedLegalDocumentQueryHandler handler = new(dbContext);

        Result<LegalDocumentResponse> result =
            await handler.Handle(
                new GetPublishedLegalDocumentQuery(
                    LegalDocumentType.PrivacyPolicy,
                    LegalAudience.Public),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(LegalAudience.Public, result.Value.Audience);
        Assert.Equal(2, result.Value.Version);
        Assert.Equal(LegalDocumentStatus.Published, result.Value.Status);
        Assert.NotEmpty(result.Value.Sections);

        // The Public pair is independent of the Family pair.
        Assert.Equal(
            1,
            dbContext.LegalDocuments.Count(document =>
                document.Audience == LegalAudience.Family &&
                document.Status == LegalDocumentStatus.Published));
    }

    [Fact]
    public async Task AnonymousGet_ReturnsThePublishedPublicDocument()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        await PublishAsync(
            dbContext,
            LegalDocumentType.TermsAndConditions,
            LegalAudience.Public);

        LegalSender sender =
            new(new GetPublishedLegalDocumentQueryHandler(dbContext));
        PublicLegalController controller = CreateController(sender);

        IActionResult result = await controller.Get(
            "TermsAndConditions",
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        LegalDocumentResponse document =
            Assert.IsType<LegalDocumentResponse>(ok.Value);
        Assert.Equal(LegalAudience.Public, document.Audience);
        Assert.Equal(
            LegalDocumentType.TermsAndConditions,
            document.DocumentType);
        Assert.NotEmpty(document.Sections);

        GetPublishedLegalDocumentQuery query =
            Assert.IsType<GetPublishedLegalDocumentQuery>(
                sender.LastRequest);
        Assert.Equal(LegalAudience.Public, query.Audience);
        Assert.Equal(
            LegalDocumentType.TermsAndConditions,
            query.DocumentType);
    }

    [Theory]
    [InlineData("1", LegalDocumentType.PrivacyPolicy)]
    [InlineData("2", LegalDocumentType.TermsAndConditions)]
    [InlineData("PrivacyPolicy", LegalDocumentType.PrivacyPolicy)]
    [InlineData("termsandconditions", LegalDocumentType.TermsAndConditions)]
    [InlineData(" 2 ", LegalDocumentType.TermsAndConditions)]
    public async Task AnonymousGet_AcceptsNumericOrEnumName(
        string documentType,
        LegalDocumentType expected)
    {
        await using CmsDbContext dbContext = CreateDbContext();
        await PublishAsync(
            dbContext,
            LegalDocumentType.PrivacyPolicy,
            LegalAudience.Public);
        await PublishAsync(
            dbContext,
            LegalDocumentType.TermsAndConditions,
            LegalAudience.Public);

        LegalSender sender =
            new(new GetPublishedLegalDocumentQueryHandler(dbContext));
        PublicLegalController controller = CreateController(sender);

        IActionResult result = await controller.Get(
            documentType,
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        LegalDocumentResponse document =
            Assert.IsType<LegalDocumentResponse>(ok.Value);
        Assert.Equal(expected, document.DocumentType);
        Assert.Equal(LegalAudience.Public, document.Audience);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0")]
    [InlineData("3")]
    [InlineData("9")]
    [InlineData("abc")]
    [InlineData("PrivacyPolicy,TermsAndConditions")]
    public async Task AnonymousGet_MalformedDocumentType_IsCodedBadRequest(
        string documentType)
    {
        await using CmsDbContext dbContext = CreateDbContext();
        await PublishAsync(
            dbContext,
            LegalDocumentType.PrivacyPolicy,
            LegalAudience.Public);

        LegalSender sender =
            new(new GetPublishedLegalDocumentQueryHandler(dbContext));
        PublicLegalController controller = CreateController(sender);

        IActionResult result = await controller.Get(
            documentType,
            CancellationToken.None);

        ObjectResult badRequest =
            Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);

        ProblemDetails problem =
            Assert.IsType<ProblemDetails>(badRequest.Value);
        Assert.Equal(
            ContentErrors.InvalidDocumentTypeCode,
            problem.Extensions["code"]);

        // A malformed type never reaches the query/handler.
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task AnonymousGet_WithoutPublishedPublicDocument_IsNotPublished404()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        // A published Family version exists, proving the anonymous read never
        // falls back to another audience.
        await PublishAsync(
            dbContext,
            LegalDocumentType.PrivacyPolicy,
            LegalAudience.Family);

        LegalSender sender =
            new(new GetPublishedLegalDocumentQueryHandler(dbContext));
        PublicLegalController controller = CreateController(sender);

        IActionResult result = await controller.Get(
            "PrivacyPolicy",
            CancellationToken.None);

        ObjectResult notFound = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);

        ProblemDetails problem =
            Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(
            LegalErrors.NotPublished.Code,
            problem.Extensions["code"]);
    }

    [Fact]
    public void Controller_IsAnonymousOnThePublicLegalRoute()
    {
        Assert.NotEmpty(
            typeof(PublicLegalController)
                .GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Empty(
            typeof(PublicLegalController)
                .GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(
            "api/v1/public/legal",
            Assert.Single(
                typeof(PublicLegalController)
                    .GetCustomAttributes<RouteAttribute>()).Template);

        Assert.Equal(
            "{documentType}",
            typeof(PublicLegalController)
                .GetMethod(nameof(PublicLegalController.Get))!
                .GetCustomAttribute<HttpGetAttribute>()!
                .Template);
    }

    [Fact]
    public async Task CreateValidator_AcceptsThePublicAudience()
    {
        var result =
            await new CreateLegalDocumentCommandValidator()
                .ValidateAsync(
                    new CreateLegalDocumentCommand(
                        LegalDocumentType.TermsAndConditions,
                        LegalAudience.Public,
                        [Section(LegalSectionType.Text, 1)]));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public async Task CreateValidator_StillRejectsAnUndefinedAudience()
    {
        var result =
            await new CreateLegalDocumentCommandValidator()
                .ValidateAsync(
                    new CreateLegalDocumentCommand(
                        LegalDocumentType.TermsAndConditions,
                        (LegalAudience)9,
                        [Section(LegalSectionType.Text, 1)]));

        Assert.False(result.IsValid);
        Assert.Contains(
            result.Errors,
            failure => failure.PropertyName == nameof(
                CreateLegalDocumentCommand.Audience));
    }

    [Fact]
    public async Task AuthenticatedAppRead_NeverServesThePublicAudience()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        await PublishAsync(
            dbContext,
            LegalDocumentType.PrivacyPolicy,
            LegalAudience.Public);

        GetPublishedLegalDocumentQueryHandler handler = new(dbContext);

        // A Family token has no Family publication, so it gets NotPublished -
        // never the Public document.
        LegalSender familySender = new(handler);
        LegalController family = CreateLegalController(
            familySender,
            AccountType.Family);

        IActionResult omitted =
            await family.GetPrivacyPolicy(CancellationToken.None);

        ObjectResult notFound = Assert.IsType<ObjectResult>(omitted);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        Assert.Equal(
            LegalErrors.NotPublished.Code,
            Assert.IsType<ProblemDetails>(notFound.Value)
                .Extensions["code"]);

        // Naming the public audience explicitly is refused exactly as before:
        // Public (0) is not an app audience on the authenticated routes.
        foreach (string audience in new[] { "0", "Public" })
        {
            familySender.LastRequest = null;

            IActionResult denied =
                await family.GetPrivacyPolicy(
                    CancellationToken.None,
                    audience);

            ObjectResult badRequest =
                Assert.IsAssignableFrom<ObjectResult>(denied);
            Assert.Equal(
                StatusCodes.Status400BadRequest,
                badRequest.StatusCode);
            Assert.Equal(
                "Cms.Content.InvalidAudience",
                Assert.IsType<ProblemDetails>(badRequest.Value)
                    .Extensions["code"]);
            Assert.Null(familySender.LastRequest);
        }

        // An admin token keeps the documented 403 denial.
        LegalSender adminSender = new(handler);
        LegalController admin = CreateLegalController(
            adminSender,
            AccountType.SuperAdmin);

        IActionResult adminResult =
            await admin.GetPrivacyPolicy(CancellationToken.None);

        ObjectResult forbidden = Assert.IsType<ObjectResult>(adminResult);
        Assert.Equal(
            StatusCodes.Status403Forbidden,
            forbidden.StatusCode);
        Assert.Equal(
            ContentErrors.UnsupportedAudience.Code,
            Assert.IsType<ProblemDetails>(forbidden.Value)
                .Extensions["code"]);
        Assert.Null(adminSender.LastRequest);
    }

    private static CreateLegalDocumentCommand PublicPrivacyCommand()
    {
        return new CreateLegalDocumentCommand(
            LegalDocumentType.PrivacyPolicy,
            LegalAudience.Public,
            [
                Section(LegalSectionType.Text, 1),
                Section(LegalSectionType.UserRights, 2)
            ]);
    }

    private static async Task PublishAsync(
        CmsDbContext dbContext,
        LegalDocumentType documentType,
        LegalAudience audience)
    {
        IReadOnlyList<LegalSectionInput> sections =
            documentType == LegalDocumentType.PrivacyPolicy
                ? [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 2)
                ]
                : [Section(LegalSectionType.Text, 1)];

        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);

        var created = await createHandler.Handle(
            new CreateLegalDocumentCommand(
                documentType,
                audience,
                sections),
            CancellationToken.None);

        Assert.True(created.IsSuccess, created.Error?.Code);

        await publishHandler.Handle(
            new PublishLegalDocumentCommand(created.Value.Id),
            CancellationToken.None);
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
            sectionType == LegalSectionType.UserRights
                ? ["حق"]
                : [],
            sectionType == LegalSectionType.UserRights
                ? ["Right"]
                : []);
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(options);
    }

    private static PublicLegalController CreateController(
        ISender sender)
    {
        return new PublicLegalController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static LegalController CreateLegalController(
        ISender sender,
        params AccountType[] accountTypes)
    {
        return new LegalController(sender)
        {
            ControllerContext = new ControllerContext
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
            }
        };
    }

    private sealed class LegalSender : ISender
    {
        private readonly GetPublishedLegalDocumentQueryHandler _handler;

        public LegalSender(
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

            throw new NotSupportedException(
                $"Unsupported request type {request.GetType().Name}.");
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
