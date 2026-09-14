using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Cms;

public sealed class LegalDocumentTests
{
    private static readonly Guid UnknownDocumentGuid =
        Guid.Parse("0f000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task Create_PrivacyPolicyRequiresExactlyOneUserRightsSection()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler handler = new(dbContext);

        var withoutUserRights = await handler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.Family,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);

        Assert.True(withoutUserRights.IsFailure);
        Assert.Equal(
            LegalErrors.InvalidOperation.Code,
            withoutUserRights.Error.Code);

        var withTwoUserRights = await handler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.Family,
                [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 2),
                    Section(LegalSectionType.UserRights, 3)
                ]),
            CancellationToken.None);

        Assert.True(withTwoUserRights.IsFailure);
        Assert.Equal(
            LegalErrors.InvalidOperation.Code,
            withTwoUserRights.Error.Code);

        // Invalid documents are rejected before anything is persisted.
        Assert.Empty(dbContext.LegalDocuments);

        var withOneUserRights = await handler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.Family,
                [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 2)
                ]),
            CancellationToken.None);

        Assert.True(withOneUserRights.IsSuccess);
        Assert.Single(dbContext.LegalDocuments);
        Assert.Equal(
            LegalDocumentStatus.Draft,
            withOneUserRights.Value.Status);

        // The shared domain rule rejects a UserRights section without
        // bullets in both languages.
        IReadOnlyList<string> shapeErrors =
            LegalDocumentShape.Validate(
                LegalDocumentType.PrivacyPolicy,
                [
                    new LegalSectionDraft(
                        LegalSectionType.UserRights,
                        1,
                        "حقوق المستخدم",
                        "User rights",
                        "وصف",
                        "Description",
                        null,
                        null)
                ]);

        Assert.Contains(
            shapeErrors,
            error => error.Contains("Arabic bullet"));
        Assert.Contains(
            shapeErrors,
            error => error.Contains("English bullet"));
    }

    [Fact]
    public async Task Create_TermsRejectsUserRightsSection()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler handler = new(dbContext);

        var result = await handler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.TermsAndConditions,
                LegalAudience.Family,
                [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 2)
                ]),
            CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            LegalErrors.InvalidOperation.Code,
            result.Error.Code);
        Assert.Empty(dbContext.LegalDocuments);

        var termsOnly = await handler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.TermsAndConditions,
                LegalAudience.Family,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);

        Assert.True(termsOnly.IsSuccess);
        Assert.Equal(
            LegalDocumentType.TermsAndConditions,
            termsOnly.Value.DocumentType);
        Assert.DoesNotContain(
            termsOnly.Value.Sections,
            section =>
                section.SectionType == LegalSectionType.UserRights);
    }

    [Fact]
    public async Task Create_AssignsNextVersionPerDocumentTypeAndAudience()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler createHandler = new(dbContext);

        var firstPrivacyFamily = await createHandler.Handle(
            PrivacyCommand(),
            CancellationToken.None);
        Assert.True(firstPrivacyFamily.IsSuccess);
        Assert.Equal(1, firstPrivacyFamily.Value.Version);

        // Publishing keeps history, so the next draft starts at 2.
        await new PublishLegalDocumentCommandHandler(dbContext).Handle(
            new PublishLegalDocumentCommand(
                firstPrivacyFamily.Value.Id),
            CancellationToken.None);

        var secondPrivacyFamily = await createHandler.Handle(
            PrivacyCommand(),
            CancellationToken.None);
        Assert.True(secondPrivacyFamily.IsSuccess);
        Assert.Equal(2, secondPrivacyFamily.Value.Version);

        // Versioning is independent for each (type, audience) pair.
        var termsFamily = await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.TermsAndConditions,
                LegalAudience.Family,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);
        Assert.Equal(1, termsFamily.Value.Version);

        var privacyCaregiver = await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.MedicalCaregiver,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);
        Assert.Equal(1, privacyCaregiver.Value.Version);

        // A second draft for the same pair conflicts.
        var duplicateDraft = await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.MedicalCaregiver,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);
        Assert.True(duplicateDraft.IsFailure);
        Assert.Equal(
            LegalErrors.DraftAlreadyExists.Code,
            duplicateDraft.Error.Code);
    }

    [Fact]
    public async Task Publish_ArchivesPreviousPublishedVersionAndRetainsHistory()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);

        var first = await createHandler.Handle(
            PrivacyCommand(),
            CancellationToken.None);
        var publishedFirst = await publishHandler.Handle(
            new PublishLegalDocumentCommand(first.Value.Id),
            CancellationToken.None);

        Assert.True(publishedFirst.IsSuccess);
        Assert.Equal(LegalDocumentStatus.Published, publishedFirst.Value.Status);
        Assert.NotNull(publishedFirst.Value.PublishedOnUtc);

        var second = await createHandler.Handle(
            PrivacyCommand(),
            CancellationToken.None);
        var publishedSecond = await publishHandler.Handle(
            new PublishLegalDocumentCommand(second.Value.Id),
            CancellationToken.None);

        Assert.True(publishedSecond.IsSuccess);
        Assert.Equal(2, publishedSecond.Value.Version);
        Assert.Equal(LegalDocumentStatus.Published, publishedSecond.Value.Status);

        await dbContext.SaveChangesAsync();

        // Exactly one Published version remains for the pair.
        Assert.Equal(
            1,
            dbContext.LegalDocuments.Count(
                document =>
                    document.DocumentType == LegalDocumentType.PrivacyPolicy &&
                    document.Audience == LegalAudience.Family &&
                    document.Status == LegalDocumentStatus.Published));

        // The previous row is archived and still retained (no deletion).
        LegalDocument archived =
            dbContext.LegalDocuments.Single(
                document => document.Id == first.Value.Id);
        Assert.Equal(LegalDocumentStatus.Archived, archived.Status);
        Assert.Equal(2, dbContext.LegalDocuments.Count());

        // Publishing an already published version is an idempotent success.
        var republish = await publishHandler.Handle(
            new PublishLegalDocumentCommand(second.Value.Id),
            CancellationToken.None);
        Assert.True(republish.IsSuccess);
        Assert.Equal(LegalDocumentStatus.Published, republish.Value.Status);

        // Publishing an archived version is rejected.
        var publishArchived = await publishHandler.Handle(
            new PublishLegalDocumentCommand(first.Value.Id),
            CancellationToken.None);
        Assert.True(publishArchived.IsFailure);
        Assert.Equal(
            LegalErrors.InvalidOperation.Code,
            publishArchived.Error.Code);

        // Unknown id is 404 NotFound (fixed non-empty fixture-safe guid).
        var publishUnknown = await publishHandler.Handle(
            new PublishLegalDocumentCommand(
                new LegalDocumentId(UnknownDocumentGuid)),
            CancellationToken.None);
        Assert.True(publishUnknown.IsFailure);
        Assert.Equal(
            LegalErrors.NotFound.Code,
            publishUnknown.Error.Code);
    }

    [Fact]
    public async Task Update_RejectsPublishedAndArchivedVersions()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);
        UpdateLegalDocumentSectionsCommandHandler updateHandler =
            new(dbContext);

        var first = await createHandler.Handle(
            PrivacyCommand(),
            CancellationToken.None);

        // Drafts are updatable.
        var draftUpdate = await updateHandler.Handle(
            new UpdateLegalDocumentSectionsCommand(
                first.Value.Id,
                [
                    Section(LegalSectionType.Text, 1),
                    Section(LegalSectionType.UserRights, 5)
                ]),
            CancellationToken.None);
        Assert.True(draftUpdate.IsSuccess);
        Assert.Equal(2, draftUpdate.Value.Sections.Count);
        Assert.Contains(
            draftUpdate.Value.Sections,
            section =>
                section.SectionType == LegalSectionType.UserRights &&
                section.DisplayOrder == 5);

        await publishHandler.Handle(
            new PublishLegalDocumentCommand(first.Value.Id),
            CancellationToken.None);

        // Published versions are immutable.
        var publishedUpdate = await updateHandler.Handle(
            new UpdateLegalDocumentSectionsCommand(
                first.Value.Id,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);
        Assert.True(publishedUpdate.IsFailure);
        Assert.Equal(
            LegalErrors.PublishedDocumentImmutable.Code,
            publishedUpdate.Error.Code);

        var second = await createHandler.Handle(
            PrivacyCommand(),
            CancellationToken.None);
        await publishHandler.Handle(
            new PublishLegalDocumentCommand(second.Value.Id),
            CancellationToken.None);

        // Archived versions are immutable too; the archived row is the
        // first version now.
        var archivedUpdate = await updateHandler.Handle(
            new UpdateLegalDocumentSectionsCommand(
                first.Value.Id,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);
        Assert.True(archivedUpdate.IsFailure);
        Assert.Equal(
            LegalErrors.PublishedDocumentImmutable.Code,
            archivedUpdate.Error.Code);
        Assert.Equal(2, dbContext.LegalDocuments.Count());

        // Unknown id is 404 NotFound.
        var unknownUpdate = await updateHandler.Handle(
            new UpdateLegalDocumentSectionsCommand(
                new LegalDocumentId(UnknownDocumentGuid),
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None);
        Assert.True(unknownUpdate.IsFailure);
        Assert.Equal(
            LegalErrors.NotFound.Code,
            unknownUpdate.Error.Code);
    }

    [Fact]
    public async Task AppRead_DerivesAudienceFromClaimsAndDoesNotFallback()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        CreateLegalDocumentCommandHandler createHandler = new(dbContext);
        PublishLegalDocumentCommandHandler publishHandler = new(dbContext);

        LegalDocumentId familyPublished = (await createHandler.Handle(
            PrivacyCommand(),
            CancellationToken.None)).Value.Id;
        await publishHandler.Handle(
            new PublishLegalDocumentCommand(familyPublished),
            CancellationToken.None);

        // A caregiver publication with a higher version number must never
        // leak into the family read and vice versa.
        LegalDocumentId caregiverPublished = (await createHandler.Handle(
            new CreateLegalDocumentCommand(
                LegalDocumentType.PrivacyPolicy,
                LegalAudience.MedicalCaregiver,
                [Section(LegalSectionType.Text, 1)]),
            CancellationToken.None)).Value.Id;
        await publishHandler.Handle(
            new PublishLegalDocumentCommand(caregiverPublished),
            CancellationToken.None);

        GetPublishedLegalDocumentQueryHandler queryHandler = new(dbContext);

        // Family claim -> audience Family is derived by the controller.
        var familySender = new ForwardingSender(queryHandler);
        LegalController familyController = CreateLegalController(
            familySender,
            AccountType.Family);

        IActionResult familyResult =
            await familyController.GetPrivacyPolicy(
                CancellationToken.None);

        GetPublishedLegalDocumentQuery sentQuery =
            Assert.IsType<GetPublishedLegalDocumentQuery>(
                familySender.LastRequest);
        Assert.Equal(LegalAudience.Family, sentQuery.Audience);
        Assert.Equal(
            LegalDocumentType.PrivacyPolicy,
            sentQuery.DocumentType);

        var okResult = Assert.IsType<OkObjectResult>(familyResult);
        LegalDocumentResponse familyDocument =
            Assert.IsType<LegalDocumentResponse>(okResult.Value);
        Assert.Equal(LegalAudience.Family, familyDocument.Audience);
        Assert.Equal(1, familyDocument.Version);

        // Companion caregiver has no publication -> NotPublished, and the
        // read never falls back to another audience or another document type.
        var caregiverSender = new ForwardingSender(queryHandler);
        LegalController caregiverController = CreateLegalController(
            caregiverSender,
            AccountType.CompanionCaregiver);

        IActionResult caregiverResult =
            await caregiverController.GetTerms(
                CancellationToken.None);

        GetPublishedLegalDocumentQuery caregiverQuery =
            Assert.IsType<GetPublishedLegalDocumentQuery>(
                caregiverSender.LastRequest);
        Assert.Equal(
            LegalAudience.CompanionCaregiver,
            caregiverQuery.Audience);
        Assert.Equal(
            LegalDocumentType.TermsAndConditions,
            caregiverQuery.DocumentType);

        var notFound = Assert.IsType<ObjectResult>(caregiverResult);
        Assert.Equal(StatusCodes.Status404NotFound, notFound.StatusCode);
        ProblemDetails problem =
            Assert.IsType<ProblemDetails>(notFound.Value);
        Assert.Equal(
            LegalErrors.NotPublished.Code,
            problem.Extensions["code"]);

        // Admin account types are not app audiences.
        var adminSender = new ForwardingSender(queryHandler);
        LegalController adminController = CreateLegalController(
            adminSender,
            AccountType.SuperAdmin);

        IActionResult adminResult =
            await adminController.GetPrivacyPolicy(
                CancellationToken.None);

        var forbidden = Assert.IsType<ObjectResult>(adminResult);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        ProblemDetails adminProblem =
            Assert.IsType<ProblemDetails>(forbidden.Value);
        Assert.Equal(
            ContentErrors.UnsupportedAudience.Code,
            adminProblem.Extensions["code"]);
        Assert.Null(adminSender.LastRequest);

        // The claims mapping itself (no client-supplied audience override).
        Assert.True(
            CreateAudienceProbe(AccountType.Elderly)
                .TryReadCmsAudience(out LegalAudience elderlyAudience));
        Assert.Equal(LegalAudience.Elderly, elderlyAudience);

        Assert.False(
            CreateAudienceProbe(AccountType.SupportAdmin)
                .TryReadCmsAudience(out _));
        Assert.False(
            CreateAudienceProbe(AccountType.ContentAdmin)
                .TryReadCmsAudience(out _));
        Assert.True(
            CreateAudienceProbe(AccountType.MedicalCaregiver)
                .TryReadCmsAudience(
                    out LegalAudience medicalAudience));
        Assert.Equal(
            LegalAudience.MedicalCaregiver,
            medicalAudience);
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
                ? ["حق الوصول"]
                : [],
            sectionType == LegalSectionType.UserRights
                ? ["Access right"]
                : []);
    }

    private static CreateLegalDocumentCommand PrivacyCommand()
    {
        return new CreateLegalDocumentCommand(
            LegalDocumentType.PrivacyPolicy,
            LegalAudience.Family,
            [
                Section(LegalSectionType.Text, 1),
                Section(LegalSectionType.UserRights, 2)
            ]);
    }

    private static LegalController CreateLegalController(
        ISender sender,
        AccountType accountType)
    {
        return new LegalController(sender)
        {
            ControllerContext = CreateContext(accountType)
        };
    }

    private static AudienceProbeController CreateAudienceProbe(
        AccountType accountType)
    {
        return new AudienceProbeController
        {
            ControllerContext = CreateContext(accountType)
        };
    }

    private static ControllerContext CreateContext(
        AccountType accountType)
    {
        return new ControllerContext
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
        };
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(options);
    }

    private sealed class AudienceProbeController : ApiControllerBase
    {
        public bool TryReadCmsAudience(out LegalAudience audience)
        {
            return TryGetCmsAudienceFromClaims(out audience);
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

        public object? LastRequest { get; private set; }

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
