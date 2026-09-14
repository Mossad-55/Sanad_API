using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Application.Legal;
using Sanad.Modules.Cms.Domain.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Infrastructure.Persistence;

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
