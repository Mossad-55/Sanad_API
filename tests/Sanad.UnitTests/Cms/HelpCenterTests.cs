using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Behaviors;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using System.Text.Json;

namespace Sanad.UnitTests.Cms;

public sealed class HelpCenterTests
{
    private static readonly Guid UnknownFaqGuid =
        Guid.Parse("0f000000-0000-4000-8000-000000000001");

    [Fact]
    public async Task AppRead_ReturnsOnlyActiveFaqsForCurrentAudience()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي نشط",
            "Active family question",
            displayOrder: 2,
            isActive: true);
        SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي نشط آخر",
            "Another active family question",
            displayOrder: 1,
            isActive: true);
        SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي معطّل",
            "Inactive family question",
            displayOrder: 0,
            isActive: false);
        SeedFaq(
            dbContext,
            LegalAudience.MedicalCaregiver,
            "سؤال مقدم رعاية",
            "Caregiver question",
            displayOrder: 0,
            isActive: true);
        await dbContext.SaveChangesAsync();

        var result = await new GetHelpCenterQueryHandler(dbContext).Handle(
            new GetHelpCenterQuery(LegalAudience.Family),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Faqs.Count);

        // Ordered by display order; inactive and other-audience entries are
        // excluded.
        Assert.Equal(1, result.Value.Faqs[0].DisplayOrder);
        Assert.Equal(2, result.Value.Faqs[1].DisplayOrder);
        Assert.All(
            result.Value.Faqs,
            faq => Assert.Equal(LegalAudience.Family, faq.Audience));
        Assert.DoesNotContain(
            result.Value.Faqs,
            faq => faq.EnglishQuestion == "Inactive family question");
        Assert.DoesNotContain(
            result.Value.Faqs,
            faq =>
                faq.Audience == LegalAudience.MedicalCaregiver);
    }

    [Fact]
    public async Task AppRead_OwnerAudience_ReturnsOnlyOwnerFaqs()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        SeedFaq(
            dbContext,
            LegalAudience.CareHomeOwner,
            "سؤال مالك",
            "Owner question",
            displayOrder: 0,
            isActive: true);
        SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي",
            "Family question",
            displayOrder: 0,
            isActive: true);
        await dbContext.SaveChangesAsync();

        var result = await new GetHelpCenterQueryHandler(dbContext).Handle(
            new GetHelpCenterQuery(LegalAudience.CareHomeOwner),
            CancellationToken.None);

        // Care Home Owner is a Help Center audience (F5); the owner read is
        // parity with the legal routes and never leaks another audience's FAQs.
        Assert.True(result.IsSuccess);
        HelpCenterFaqItem ownerFaq = Assert.Single(result.Value.Faqs);
        Assert.Equal(LegalAudience.CareHomeOwner, ownerFaq.Audience);
        Assert.Equal("Owner question", ownerFaq.EnglishQuestion);
    }

    [Fact]
    public async Task AppRead_ReturnsGlobalSupportContactWhenConfigured()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        var upserted = await new UpsertSupportContactCommandHandler(
            dbContext).Handle(
            new UpsertSupportContactCommand(
                "+201000000001",
                "support@sanad.example"),
            CancellationToken.None);

        Assert.True(upserted.IsSuccess);

        var familyRead = await new GetHelpCenterQueryHandler(dbContext)
            .Handle(
                new GetHelpCenterQuery(LegalAudience.Family),
                CancellationToken.None);
        var caregiverRead = await new GetHelpCenterQueryHandler(dbContext)
            .Handle(
                new GetHelpCenterQuery(
                    LegalAudience.CompanionCaregiver),
                CancellationToken.None);

        Assert.True(familyRead.IsSuccess);
        Assert.True(caregiverRead.IsSuccess);

        // One global pair for every app role.
        Assert.NotNull(familyRead.Value.SupportContact);
        Assert.Equal(
            familyRead.Value.SupportContact!.SupportPhone,
            caregiverRead.Value.SupportContact!.SupportPhone);
        Assert.Equal(
            familyRead.Value.SupportContact.SupportEmail,
            caregiverRead.Value.SupportContact.SupportEmail);
        Assert.Equal(
            "+201000000001",
            familyRead.Value.SupportContact.SupportPhone);
    }

    [Fact]
    public async Task
        AppRead_ReturnsEmptyFaqsAndNullContactBeforeContactConfiguration()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        var result = await new GetHelpCenterQueryHandler(dbContext).Handle(
            new GetHelpCenterQuery(LegalAudience.Elderly),
            CancellationToken.None);

        // The read surface is 200 and non-destructive before any seeding.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Faqs);
        Assert.Null(result.Value.SupportContact);
        Assert.Empty(dbContext.HelpFaqs);
        Assert.Empty(dbContext.SupportContacts);
    }

    [Fact]
    public async Task UpsertSupportContact_MaintainsOneGlobalRow()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        UpsertSupportContactCommandHandler handler = new(dbContext);

        var first = await handler.Handle(
            new UpsertSupportContactCommand(
                "+201000000001",
                "first@sanad.example"),
            CancellationToken.None);
        var second = await handler.Handle(
            new UpsertSupportContactCommand(
                "+201000000002",
                "second@sanad.example"),
            CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        // Repeated PUT updates the same singleton, never a second row.
        Assert.Single(dbContext.SupportContacts);
        Assert.Equal(
            "+201000000002",
            second.Value.SupportPhone);

        var adminRead = await new GetSupportContactQueryHandler(dbContext)
            .Handle(
                new GetSupportContactQuery(),
                CancellationToken.None);
        Assert.True(adminRead.IsSuccess);
        Assert.Equal(
            "second@sanad.example",
            adminRead.Value.SupportEmail);
    }

    [Fact]
    public async Task ActivateAndDeactivate_AreIdempotent()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        HelpFaqId faqId = SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال",
            "Question",
            displayOrder: 0,
            isActive: true);
        await dbContext.SaveChangesAsync();

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var deactivated =
                await new DeactivateHelpFaqCommandHandler(dbContext).Handle(
                    new DeactivateHelpFaqCommand(faqId),
                    CancellationToken.None);

            Assert.True(deactivated.IsSuccess);
            Assert.False(deactivated.Value.IsActive);
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            var activated =
                await new ActivateHelpFaqCommandHandler(dbContext).Handle(
                    new ActivateHelpFaqCommand(faqId),
                    CancellationToken.None);

            Assert.True(activated.IsSuccess);
            Assert.True(activated.Value.IsActive);
        }

        // The FAQ row is preserved, not deleted.
        Assert.Single(dbContext.HelpFaqs);
        Assert.Equal(
            "Question",
            dbContext.HelpFaqs.Single().EnglishQuestion);

        // Unknown ids return 404 Cms.Help.FaqNotFound.
        var unknown = await new ActivateHelpFaqCommandHandler(dbContext)
            .Handle(
                new ActivateHelpFaqCommand(
                    new HelpFaqId(UnknownFaqGuid)),
                CancellationToken.None);
        Assert.True(unknown.IsFailure);
        Assert.Equal(
            HelpErrors.FaqNotFound.Code,
            unknown.Error.Code);
    }

    [Fact]
    public async Task CreateFaq_ReturnsRecordAndAdminListIncludesInactive()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        var created = await new CreateHelpFaqCommandHandler(dbContext)
            .Handle(
                new CreateHelpFaqCommand(
                    LegalAudience.Family,
                    "سؤال",
                    "Question",
                    "إجابة",
                    "Answer",
                    0,
                    false),
                CancellationToken.None);

        Assert.True(created.IsSuccess);
        Assert.False(created.Value.IsActive);

        var adminList = await new ListHelpFaqsQueryHandler(dbContext).Handle(
            new ListHelpFaqsQuery(null, null),
            CancellationToken.None);
        Assert.Single(adminList.Value);

        var activeOnly =
            await new ListHelpFaqsQueryHandler(dbContext).Handle(
                new ListHelpFaqsQuery(
                    LegalAudience.Family,
                    true),
                CancellationToken.None);
        Assert.Empty(activeOnly.Value);

        // The app read never returns inactive records.
        var appRead = await new GetHelpCenterQueryHandler(dbContext).Handle(
            new GetHelpCenterQuery(LegalAudience.Family),
            CancellationToken.None);
        Assert.Empty(appRead.Value.Faqs);
    }

    [Fact]
    public async Task FaqLengthLimits_AreEnforcedByDomain()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        await Assert.ThrowsAnyAsync<Exception>(
            async () => await new CreateHelpFaqCommandHandler(dbContext)
                .Handle(
                    new CreateHelpFaqCommand(
                        LegalAudience.Family,
                        "س",
                        new string('Q',
                            HelpFaq.MaximumQuestionLength + 1),
                        "إجابة",
                        "Answer",
                        0,
                        true),
                    CancellationToken.None));

        Assert.Empty(dbContext.HelpFaqs);

        // Display order may be zero but never negative.
        await Assert.ThrowsAnyAsync<Exception>(
            async () => await new CreateHelpFaqCommandHandler(dbContext)
                .Handle(
                    new CreateHelpFaqCommand(
                        LegalAudience.Family,
                        "سؤال",
                        "Question",
                        "إجابة",
                        "Answer",
                        -1,
                        true),
                    CancellationToken.None));

        Assert.Empty(dbContext.HelpFaqs);
    }

    [Theory]
    [InlineData(LegalAudience.Family)]
    [InlineData(LegalAudience.MedicalCaregiver)]
    [InlineData(LegalAudience.CompanionCaregiver)]
    [InlineData(LegalAudience.Elderly)]
    public async Task CreateFaqValidator_AcceptsSupportedAppAudiences(
        LegalAudience audience)
    {
        var result = await new CreateHelpFaqCommandValidator()
            .ValidateAsync(ValidCreateCommand(audience));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData(LegalAudience.Family)]
    [InlineData(LegalAudience.MedicalCaregiver)]
    [InlineData(LegalAudience.CompanionCaregiver)]
    [InlineData(LegalAudience.Elderly)]
    public async Task UpdateFaqValidator_AcceptsSupportedAppAudiences(
        LegalAudience audience)
    {
        var result = await new UpdateHelpFaqCommandValidator()
            .ValidateAsync(
                ValidUpdateCommand(
                    new HelpFaqId(UnknownFaqGuid),
                    audience));

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Theory]
    [InlineData((LegalAudience)9)]
    public async Task CreateFaqValidator_RejectsUnsupportedAudienceOnAudienceField(
        LegalAudience audience)
    {
        var result = await new CreateHelpFaqCommandValidator()
            .ValidateAsync(ValidCreateCommand(audience));

        Assert.False(result.IsValid);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal("Audience", failure.PropertyName, ignoreCase: true);
        Assert.Contains(
            "Audience must be 1 (Family)",
            failure.ErrorMessage);
    }

    [Theory]
    [InlineData((LegalAudience)9)]
    public async Task UpdateFaqValidator_RejectsUnsupportedAudienceOnAudienceField(
        LegalAudience audience)
    {
        var result = await new UpdateHelpFaqCommandValidator()
            .ValidateAsync(
                ValidUpdateCommand(
                    new HelpFaqId(UnknownFaqGuid),
                    audience));

        Assert.False(result.IsValid);

        ValidationFailure failure = Assert.Single(result.Errors);
        Assert.Equal("Audience", failure.PropertyName, ignoreCase: true);
        Assert.Contains(
            "Audience must be 1 (Family)",
            failure.ErrorMessage);
    }

    [Fact]
    public async Task CreateFaq_UnsupportedAudience_IsRejectedBeforeAnyRowIsCreated()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        ValidationException exception =
            await Assert.ThrowsAsync<ValidationException>(
                () => RunCreatePipelineAsync(
                    dbContext,
                    (LegalAudience)9));

        // The request field is named, so a consumer can map the coded failure
        // back to the offending property instead of seeing an unexpected 500
        // from the HelpFaq domain guard.
        Assert.Contains(
            exception.Errors,
            failure => failure.PropertyName == "Audience");
        Assert.Empty(dbContext.HelpFaqs);

        ProblemSummary problem = await SummarizeAsync(exception);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.Status);
        Assert.Equal("Api.Validation.Failed", problem.Code);
        Assert.Contains("Audience", problem.Fields);
    }

    [Fact]
    public async Task UpdateFaq_UnsupportedAudience_IsRejectedAndLeavesStoredRowUnchanged()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        HelpFaqId faqId = SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال",
            "Question",
            displayOrder: 0,
            isActive: true);
        await dbContext.SaveChangesAsync();

        var behavior =
            new ValidationBehavior<UpdateHelpFaqCommand,
                Result<HelpFaqResponse>>(
                [new UpdateHelpFaqCommandValidator()]);
        UpdateHelpFaqCommand command =
            new(
                faqId,
                (LegalAudience)9,
                "سؤال محدّث",
                "Updated question",
                "إجابة محدّثة",
                "Updated answer",
                5,
                false);

        ValidationException exception =
            await Assert.ThrowsAsync<ValidationException>(
                () => behavior.Handle(
                    command,
                    _ => new UpdateHelpFaqCommandHandler(dbContext)
                        .Handle(command, CancellationToken.None),
                    CancellationToken.None));

        Assert.Contains(
            exception.Errors,
            failure => failure.PropertyName == "Audience");

        HelpFaq stored = dbContext.HelpFaqs.Single();
        Assert.Equal(LegalAudience.Family, stored.Audience);
        Assert.Equal("Question", stored.EnglishQuestion);
        Assert.Equal(0, stored.DisplayOrder);
        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task CreateAndUpdateFaq_ElderlyAudience_RetainSupportedBehavior()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        var created = await RunCreatePipelineAsync(
            dbContext,
            LegalAudience.Elderly);
        Assert.True(created.IsSuccess);
        Assert.Equal(LegalAudience.Elderly, created.Value.Audience);

        var behavior =
            new ValidationBehavior<UpdateHelpFaqCommand,
                Result<HelpFaqResponse>>(
                [new UpdateHelpFaqCommandValidator()]);
        UpdateHelpFaqCommand command =
            new(
                created.Value.Id,
                LegalAudience.Elderly,
                "سؤال محدّث",
                "Updated question",
                "إجابة محدّثة",
                "Updated answer",
                3,
                true);

        var updated = await behavior.Handle(
            command,
            _ => new UpdateHelpFaqCommandHandler(dbContext)
                .Handle(command, CancellationToken.None),
            CancellationToken.None);

        Assert.True(updated.IsSuccess);
        Assert.Equal(LegalAudience.Elderly, updated.Value.Audience);
        Assert.Equal("Updated question", updated.Value.EnglishQuestion);

        HelpFaq stored = dbContext.HelpFaqs.Single();
        Assert.Equal(LegalAudience.Elderly, stored.Audience);
        Assert.Equal(3, stored.DisplayOrder);
    }

    [Fact]
    public async Task AdminList_TiedAudienceAndDisplayOrder_OrdersDeterministicallyById()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        // Two active family rows share their display order — the exact
        // live state that crashed the admin list before HelpFaqId became
        // comparable (the tie-breaker compared the id and the struct was
        // not IComparable).
        HelpFaqId first = SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي أ",
            "Family question one",
            displayOrder: 2,
            isActive: true);
        HelpFaqId second = SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي ب",
            "Family question two",
            displayOrder: 2,
            isActive: true);
        SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي ج",
            "Family question three",
            displayOrder: 1,
            isActive: true);
        SeedFaq(
            dbContext,
            LegalAudience.MedicalCaregiver,
            "سؤال مقدم رعاية",
            "Caregiver question",
            displayOrder: 0,
            isActive: true);
        await dbContext.SaveChangesAsync();

        HelpFaqId expectedFirst =
            first.Value.CompareTo(second.Value) < 0 ? first : second;
        HelpFaqId expectedSecond =
            expectedFirst == first ? second : first;

        var unfiltered = await new ListHelpFaqsQueryHandler(dbContext)
            .Handle(
                new ListHelpFaqsQuery(null, null),
                CancellationToken.None);

        // Unfiltered: the whole set, ordered by audience, then display
        // order, then id — deterministic, never the 500 compare failure.
        Assert.True(unfiltered.IsSuccess);
        Assert.Equal(4, unfiltered.Value.Count);
        Assert.Equal(1, unfiltered.Value[0].DisplayOrder);
        Assert.Equal(2, unfiltered.Value[1].DisplayOrder);
        Assert.Equal(2, unfiltered.Value[2].DisplayOrder);
        Assert.Equal(expectedFirst, unfiltered.Value[1].Id);
        Assert.Equal(expectedSecond, unfiltered.Value[2].Id);

        var familyOnly = await new ListHelpFaqsQueryHandler(dbContext)
            .Handle(
                new ListHelpFaqsQuery(LegalAudience.Family, true),
                CancellationToken.None);

        Assert.True(familyOnly.IsSuccess);
        Assert.Equal(3, familyOnly.Value.Count);
        Assert.Equal(expectedFirst, familyOnly.Value[1].Id);
        Assert.Equal(expectedSecond, familyOnly.Value[2].Id);
    }

    [Fact]
    public async Task AppRead_TiedDisplayOrder_OrdersDeterministicallyById()
    {
        await using CmsDbContext dbContext = CreateDbContext();

        HelpFaqId first = SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي أ",
            "Family question one",
            displayOrder: 5,
            isActive: true);
        HelpFaqId second = SeedFaq(
            dbContext,
            LegalAudience.Family,
            "سؤال عائلي ب",
            "Family question two",
            displayOrder: 5,
            isActive: true);
        await dbContext.SaveChangesAsync();

        // The app read shares the same ordering code; tied display orders
        // must resolve on the id deterministically instead of throwing.
        var appRead = await new GetHelpCenterQueryHandler(dbContext)
            .Handle(
                new GetHelpCenterQuery(LegalAudience.Family),
                CancellationToken.None);

        Assert.True(appRead.IsSuccess);
        Assert.Equal(2, appRead.Value.Faqs.Count);
        HelpFaqId expectedFirst =
            first.Value.CompareTo(second.Value) < 0 ? first : second;
        Assert.Equal(
            expectedFirst,
            appRead.Value.Faqs[0].Id);
    }

    private static CreateHelpFaqCommand ValidCreateCommand(
        LegalAudience audience) =>
        new(
            audience,
            "سؤال",
            "Question",
            "إجابة",
            "Answer",
            0,
            true);

    private static UpdateHelpFaqCommand ValidUpdateCommand(
        HelpFaqId id,
        LegalAudience audience) =>
        new(
            id,
            audience,
            "سؤال",
            "Question",
            "إجابة",
            "Answer",
            0,
            true);

    private static Task<Result<HelpFaqResponse>> RunCreatePipelineAsync(
        CmsDbContext dbContext,
        LegalAudience audience)
    {
        var behavior =
            new ValidationBehavior<CreateHelpFaqCommand,
                Result<HelpFaqResponse>>(
                [new CreateHelpFaqCommandValidator()]);
        CreateHelpFaqCommand command = ValidCreateCommand(audience);

        // Mirrors the production MediatR pipeline: the validator runs before
        // the handler, so a rejected audience never reaches the domain.
        return behavior.Handle(
            command,
            _ => new CreateHelpFaqCommandHandler(dbContext)
                .Handle(command, CancellationToken.None),
            CancellationToken.None);
    }

    private static async Task<ProblemSummary> SummarizeAsync(
        ValidationException exception)
    {
        var httpContext = new DefaultHttpContext();

        httpContext.Request.Path = "/api/v1/admin/cms/help/faqs";
        httpContext.Response.Body = new MemoryStream();

        bool handled = await new ValidationExceptionHandler()
            .TryHandleAsync(
                httpContext,
                exception,
                CancellationToken.None);
        Assert.True(handled);

        httpContext.Response.Body.Position = 0;

        using JsonDocument document = await JsonDocument.ParseAsync(
            httpContext.Response.Body);
        JsonElement root = document.RootElement;
        JsonElement errors = root.GetProperty("errors");

        return new ProblemSummary(
            httpContext.Response.StatusCode,
            root.GetProperty("code").GetString() ?? string.Empty,
            errors.EnumerateObject().Select(x => x.Name).ToArray());
    }

    private sealed record ProblemSummary(
        int Status,
        string Code,
        IReadOnlyList<string> Fields);

    private static HelpFaqId SeedFaq(
        CmsDbContext dbContext,
        LegalAudience audience,
        string arabicQuestion,
        string englishQuestion,
        int displayOrder,
        bool isActive)
    {
        HelpFaq faq = HelpFaq.Create(
            audience,
            arabicQuestion,
            englishQuestion,
            "إجابة",
            "Answer",
            displayOrder,
            isActive);

        dbContext.HelpFaqs.Add(faq);

        return faq.Id;
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(options);
    }
}
