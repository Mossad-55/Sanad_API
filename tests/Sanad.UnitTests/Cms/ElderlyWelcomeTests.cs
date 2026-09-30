using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Application.Welcome;
using Sanad.Modules.Cms.Domain.Welcome;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class ElderlyWelcomeTests
{
    [Theory]
    [InlineData(null, "English", "ابدأ", "Start")]
    [InlineData(" ", "English", "ابدأ", "Start")]
    [InlineData("العنوان", null, "ابدأ", "Start")]
    [InlineData("العنوان", "English", "", "Start")]
    [InlineData("العنوان", "English", "ابدأ", " ")]
    public void Create_RequiresAllLocalizedHeadlineAndCtaFields(string? ar, string? en, string? arCta, string? enCta) =>
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create(ar!, en!, arCta!, enCta!, [Benefit(1)]));

    [Theory]
    [InlineData(0)]
    [InlineData(9)]
    public void Create_RequiresOneToEightBenefits(int count)
    {
        var benefits = Enumerable.Range(1, count).Select(Benefit).ToArray();
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("العنوان", "Headline", "ابدأ", "Start", benefits));
    }

    [Fact]
    public void Create_AcceptsEightBenefitsAndSortsByUniquePositiveOrder()
    {
        var benefits = Enumerable.Range(1, 8).Reverse().Select(Benefit).ToArray();
        var welcome = ElderlyWelcome.Create(" العنوان ", " Headline ", " ابدأ ", " Start ", benefits);

        Assert.Equal("العنوان", welcome.ArabicHeadline);
        Assert.Equal("Start", welcome.EnglishCtaLabel);
        Assert.Equal(Enumerable.Range(1, 8), welcome.Benefits.Select(x => x.DisplayOrder));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_RejectsNonPositiveBenefitOrder(int order) =>
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", "ar cta", "en cta", [Benefit(order)]));

    [Fact]
    public void Create_RejectsDuplicateBenefitOrder() =>
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", "ar cta", "en cta", [Benefit(1), Benefit(1)]));

    [Theory]
    [InlineData(null, "en", "ar description", "en description")]
    [InlineData(" ", "en", "ar description", "en description")]
    [InlineData("ar", null, "ar description", "en description")]
    [InlineData("ar", "en", "", "en description")]
    [InlineData("ar", "en", "ar description", " ")]
    public void Create_RequiresAllLocalizedBenefitFields(string? ar, string? en, string? arDescription, string? enDescription) =>
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("العنوان", "Headline", "ابدأ", "Start", [new(1, ar!, en!, arDescription!, enDescription!)]));

    [Fact]
    public void Create_EnforcesMaximumLengthOnLocalizedHeadlineCtaAndBenefitText()
    {
        var overlong = new string('x', ElderlyWelcome.MaximumTextLength + 1);

        Assert.Throws<DomainException>(() => ElderlyWelcome.Create(overlong, "en", "ar cta", "en cta", [Benefit(1)]));
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", overlong, "ar cta", "en cta", [Benefit(1)]));
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", overlong, "en cta", [Benefit(1)]));
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", "ar cta", overlong, [Benefit(1)]));
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", "ar cta", "en cta", [new(1, overlong, "en", "ar description", "en description")]));
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", "ar cta", "en cta", [new(1, "ar", overlong, "ar description", "en description")]));
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", "ar cta", "en cta", [new(1, "ar", "en", overlong, "en description")]));
        Assert.Throws<DomainException>(() => ElderlyWelcome.Create("ar", "en", "ar cta", "en cta", [new(1, "ar", "en", "ar description", overlong)]));
    }

    [Fact]
    public void PublicationLifecycle_IsIdempotentAndRestrictsEditsWhilePublished()
    {
        var welcome = ElderlyWelcome.Create("ar", "en", "ar cta", "en cta", [Benefit(1)]);
        Assert.Equal(ElderlyWelcomePublicationStatus.Draft, welcome.Status);
        welcome.Unpublish();
        Assert.Equal(ElderlyWelcomePublicationStatus.Draft, welcome.Status);

        welcome.Publish();
        var publishedOn = welcome.PublishedOnUtc;
        Assert.Equal(ElderlyWelcomePublicationStatus.Published, welcome.Status);
        Assert.NotNull(publishedOn);
        welcome.Publish();
        Assert.Equal(ElderlyWelcomePublicationStatus.Published, welcome.Status);
        Assert.Throws<DomainException>(() => welcome.Update("new ar", "new en", "new ar cta", "new en cta", [Benefit(1)]));

        welcome.Unpublish();
        Assert.Equal(ElderlyWelcomePublicationStatus.Draft, welcome.Status);
        welcome.Unpublish();
        Assert.Equal(ElderlyWelcomePublicationStatus.Draft, welcome.Status);
        welcome.Publish();
        Assert.Equal(publishedOn, welcome.PublishedOnUtc);
    }

    [Fact]
    public async Task PublishedQuery_ExcludesDraftAndProjectsOrderedArabicAndEnglishContent()
    {
        await using var db = CreateDbContext();
        var welcome = ElderlyWelcome.Create("مرحباً", "Welcome", "ابدأ", "Start", [Benefit(2), Benefit(1)]);
        db.ElderlyWelcomes.Add(welcome);
        await db.SaveChangesAsync();

        var handler = new GetPublishedElderlyWelcomeQueryHandler(db);
        var draftResult = await handler.Handle(new("ar"), CancellationToken.None);
        Assert.True(draftResult.IsFailure);
        Assert.Equal(ElderlyWelcomeErrors.NotPublished.Code, draftResult.Error.Code);

        welcome.Publish();
        await db.SaveChangesAsync();
        var arabic = await handler.Handle(new("AR"), CancellationToken.None);
        var english = await handler.Handle(new("en"), CancellationToken.None);

        Assert.True(arabic.IsSuccess);
        Assert.Equal("ar", arabic.Value.Language);
        Assert.Equal("مرحباً", arabic.Value.Headline);
        Assert.Equal("ابدأ", arabic.Value.CtaLabel);
        Assert.Equal("elderly.request-otp", arabic.Value.CtaAction);
        Assert.Equal([1, 2], arabic.Value.Benefits.Select(x => x.DisplayOrder));
        Assert.Equal("العنوان 1", arabic.Value.Benefits[0].Title);
        Assert.Equal("الوصف 1", arabic.Value.Benefits[0].Description);
        Assert.True(english.IsSuccess);
        Assert.Equal("Welcome", english.Value.Headline);
        Assert.Equal("Start", english.Value.CtaLabel);
        Assert.Equal("elderly.request-otp", english.Value.CtaAction);
        Assert.Equal("Title 1", english.Value.Benefits[0].Title);
        Assert.Equal("Description 1", english.Value.Benefits[0].Description);
    }

    [Fact]
    public async Task SingletonHandlers_CreateOnceAndUpdateExistingDraft()
    {
        await using var db = CreateDbContext();
        var handler = new CreateElderlyWelcomeCommandHandler(db);
        var command = CreateCommand("first");

        var created = await handler.Handle(command, CancellationToken.None);
        var duplicate = await handler.Handle(command, CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal(ElderlyWelcome.SingletonId, created.Value.Id);
        Assert.True(duplicate.IsFailure);
        Assert.Equal(ElderlyWelcomeErrors.AlreadyExists.Code, duplicate.Error.Code);
        Assert.Single(db.ElderlyWelcomes);

        var updated = await new UpdateElderlyWelcomeCommandHandler(db).Handle(
            new("updated ar", "updated en", "continue ar", "continue en", [InputBenefit(1)]), CancellationToken.None);
        Assert.True(updated.IsSuccess);
        Assert.Equal("updated en", updated.Value.EnglishHeadline);
        Assert.Single(db.ElderlyWelcomes);
    }

    private static ElderlyWelcomeBenefitDraft Benefit(int order) => new(order, $"العنوان {order}", $"Title {order}", $"الوصف {order}", $"Description {order}");
    private static ElderlyWelcomeBenefitInput InputBenefit(int order) => new(order, $"العنوان {order}", $"Title {order}", $"الوصف {order}", $"Description {order}");
    private static CreateElderlyWelcomeCommand CreateCommand(string value) => new(value, value, value, value, [InputBenefit(1)]);
    private static CmsDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CmsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
