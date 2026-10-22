using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Cms.Application.Settings;
using Sanad.Modules.Cms.Domain.Settings;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

/// <summary>
/// The admin authoring surface of the one site-settings row. The tests drive
/// the real command and query handlers against a real CMS context, so the
/// stored row, the single-row invariant and the refused writes are the real
/// behaviour and not a DTO passthrough.
/// </summary>
public sealed class SiteSettingsAdminTests
{
    [Fact]
    public async Task Authoring_creates_the_single_row_and_the_next_write_updates_that_same_row()
    {
        await using CmsDbContext db = CreateContext();
        var handler = new UpdateSiteSettingsCommandHandler(db);

        var created = await handler.Handle(
            Command(
                Links(("library", "المكتبة", "Library", "/library")),
                Seo("سند", "Sanad")),
            default);

        Assert.True(created.IsSuccess);
        Assert.Single(await db.SiteSettings.ToListAsync());
        SiteSettings first = await db.SiteSettings.SingleAsync();
        Assert.Equal(SiteSettingsId.Singleton, first.Id);
        Assert.Equal(SiteSettingsId.SingletonKey, first.Id.Value);

        var updated = await handler.Handle(
            Command(
                Links(("help", "المساعدة", "Help", "/help")),
                Seo("سند للعائلات", "Sanad for families")),
            default);

        Assert.True(updated.IsSuccess);

        // The update replaced the content of the same row: no second row.
        List<SiteSettings> rows = await db.SiteSettings.ToListAsync();
        SiteSettings single = Assert.Single(rows);
        Assert.Equal(first.Id, single.Id);
        Assert.Equal("سند للعائلات", single.ArabicSeoTitle);
        Assert.Equal("Sanad for families", single.EnglishSeoTitle);
        Assert.Equal(SiteSettingsId.Singleton, single.Id);
    }

    [Fact]
    public async Task Authoring_keeps_the_authored_link_order_and_the_bilingual_values()
    {
        await using CmsDbContext db = CreateContext();

        var result = await new UpdateSiteSettingsCommandHandler(db).Handle(
            Command(
                Links(
                    ("library", "المكتبة", "Library", "/library"),
                    ("help", "المساعدة", "Help", "/help"),
                    ("about", "من نحن", "About", "https://sanad.example/about")),
                Seo("سند", "Sanad", "وصف", "Description")),
            default);

        Assert.True(result.IsSuccess);

        IReadOnlyList<SiteSettingsNavbarLink> links = result.Value.NavbarLinks;
        Assert.Equal(3, links.Count);
        Assert.Equal(["library", "help", "about"], links.Select(link => link.Key));
        Assert.Equal("المكتبة", links[0].ArabicLabel);
        Assert.Equal("Library", links[0].EnglishLabel);
        Assert.Equal("/library", links[0].Url);
        Assert.Equal("المساعدة", links[1].ArabicLabel);
        Assert.Equal("Help", links[1].EnglishLabel);
        Assert.Equal("https://sanad.example/about", links[2].Url);

        Assert.NotNull(result.Value.Seo);
        Assert.Equal("سند", result.Value.Seo!.ArabicTitle);
        Assert.Equal("Sanad", result.Value.Seo.EnglishTitle);
        Assert.Equal("وصف", result.Value.Seo.ArabicDescription);
        Assert.Equal("Description", result.Value.Seo.EnglishDescription);

        // The stored row keeps the authored order and the authored values.
        SiteSettings stored = await db.SiteSettings.SingleAsync();
        var storedRead = await new GetAdminSiteSettingsQueryHandler(db)
            .Handle(new GetAdminSiteSettingsQuery(), default);
        Assert.True(storedRead.IsSuccess);
        Assert.Equal(["library", "help", "about"], storedRead.Value.NavbarLinks.Select(link => link.Key));
        Assert.Equal("المكتبة", storedRead.Value.NavbarLinks[0].ArabicLabel);
        Assert.Contains("المكتبة", stored.NavbarLinksJson, StringComparison.Ordinal);
        Assert.True(
            stored.NavbarLinksJson.IndexOf("library", StringComparison.Ordinal) <
            stored.NavbarLinksJson.IndexOf("help", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Authoring_refuses_an_unusable_link_target_and_does_not_mutate()
    {
        await using CmsDbContext db = CreateContext();
        var handler = new UpdateSiteSettingsCommandHandler(db);

        string[] refusedTargets =
        [
            "javascript:alert(1)",
            "data:text/html;base64,PHNjcmlwdD4=",
            "//evil.example.com",
            "/\\evil.example.com",
            "library",
            "mailto:info@sanad.example"
        ];

        foreach (string target in refusedTargets)
        {
            var result = await handler.Handle(
                Command(Links(("library", "المكتبة", "Library", target))),
                default);

            Assert.False(result.IsSuccess);
            Assert.Equal(SiteSettingsErrors.InvalidCode, result.Error.Code);
            Assert.Empty(await db.SiteSettings.ToListAsync());
        }
    }

    [Fact]
    public async Task Authoring_refuses_oversized_and_duplicated_content_and_does_not_mutate()
    {
        await using CmsDbContext db = CreateContext();
        var handler = new UpdateSiteSettingsCommandHandler(db);

        var longLabel = await handler.Handle(
            Command(Links(("library", new string('ا', SiteSettings.MaximumLinkLabelLength + 1), "Library", "/library"))),
            default);
        Assert.False(longLabel.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, longLabel.Error.Code);

        var longUrl = await handler.Handle(
            Command(Links(("library", "المكتبة", "Library", "/" + new string('a', SiteSettings.MaximumLinkUrlLength)))),
            default);
        Assert.False(longUrl.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, longUrl.Error.Code);

        var longTitle = await handler.Handle(
            Command(
                Links(("library", "المكتبة", "Library", "/library")),
                Seo(new string('a', SiteSettings.MaximumSeoTitleLength + 1), "Sanad")),
            default);
        Assert.False(longTitle.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, longTitle.Error.Code);

        var longDescription = await handler.Handle(
            Command(
                Links(("library", "المكتبة", "Library", "/library")),
                Seo("سند", "Sanad", null, new string('a', SiteSettings.MaximumSeoDescriptionLength + 1))),
            default);
        Assert.False(longDescription.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, longDescription.Error.Code);

        var duplicatedKey = await handler.Handle(
            Command(Links(
                ("library", "المكتبة", "Library", "/library"),
                ("library", "مكتبة أخرى", "Another library", "/library-2"))),
            default);
        Assert.False(duplicatedKey.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, duplicatedKey.Error.Code);

        var tooManyLinks = await handler.Handle(
            Command(Links(Enumerable.Range(0, SiteSettings.MaximumLinkCount + 1)
                .Select(index => ($"link-{index}", "رابط", "Link", $"/link-{index}"))
                .ToArray())),
            default);
        Assert.False(tooManyLinks.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, tooManyLinks.Error.Code);

        // Nothing was stored by any refused write.
        Assert.Empty(await db.SiteSettings.ToListAsync());
    }

    [Fact]
    public async Task An_invalid_update_leaves_the_authored_row_unchanged()
    {
        await using CmsDbContext db = CreateContext();
        var handler = new UpdateSiteSettingsCommandHandler(db);

        var authored = await handler.Handle(
            Command(
                Links(("library", "المكتبة", "Library", "/library")),
                Seo("سند", "Sanad", "وصف", "Description")),
            default);
        Assert.True(authored.IsSuccess);

        SiteSettings stored = await db.SiteSettings.SingleAsync();
        string originalLinksJson = stored.NavbarLinksJson;
        DateTime originalUpdatedOnUtc = stored.UpdatedOnUtc;

        // An unusable target is refused.
        var badTarget = await handler.Handle(
            Command(Links(("help", "المساعدة", "Help", "javascript:alert(1)"))),
            default);
        Assert.False(badTarget.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, badTarget.Error.Code);

        // An update whose links are valid but whose SEO title is out of
        // bounds is refused too: the links must not be written either.
        var oversizedSeo = await handler.Handle(
            Command(
                Links(("help", "المساعدة", "Help", "/help")),
                Seo(new string('a', SiteSettings.MaximumSeoTitleLength + 1), "Sanad")),
            default);
        Assert.False(oversizedSeo.IsSuccess);
        Assert.Equal(SiteSettingsErrors.InvalidCode, oversizedSeo.Error.Code);

        // The authored row is unchanged: no link, no SEO value and no
        // timestamp moved.
        SiteSettings unchanged = await db.SiteSettings.SingleAsync();
        Assert.Equal(originalLinksJson, unchanged.NavbarLinksJson);
        Assert.Equal("سند", unchanged.ArabicSeoTitle);
        Assert.Equal("Sanad", unchanged.EnglishSeoTitle);
        Assert.Equal("وصف", unchanged.ArabicSeoDescription);
        Assert.Equal("Description", unchanged.EnglishSeoDescription);
        Assert.Equal(originalUpdatedOnUtc, unchanged.UpdatedOnUtc);

        // The stored document still reads back exactly as it was authored.
        var read = await new GetAdminSiteSettingsQueryHandler(db)
            .Handle(new GetAdminSiteSettingsQuery(), default);
        Assert.True(read.IsSuccess);
        Assert.Equal("library", Assert.Single(read.Value.NavbarLinks).Key);
        Assert.Equal("/library", read.Value.NavbarLinks[0].Url);
        Assert.Equal("سند", read.Value.Seo!.ArabicTitle);
        Assert.Equal("Description", read.Value.Seo.EnglishDescription);
    }

    [Fact]
    public async Task Admin_read_reports_not_found_before_anything_is_authored_and_the_row_afterwards()
    {
        await using CmsDbContext db = CreateContext();
        var query = new GetAdminSiteSettingsQueryHandler(db);

        var missing = await query.Handle(new GetAdminSiteSettingsQuery(), default);
        Assert.False(missing.IsSuccess);
        Assert.Equal(SiteSettingsErrors.NotFoundCode, missing.Error.Code);

        var written = await new UpdateSiteSettingsCommandHandler(db).Handle(
            Command(Links(("library", "المكتبة", "Library", "/library"))),
            default);
        Assert.True(written.IsSuccess);

        var found = await query.Handle(new GetAdminSiteSettingsQuery(), default);
        Assert.True(found.IsSuccess);
        Assert.Single(found.Value.NavbarLinks);
        Assert.Equal("library", found.Value.NavbarLinks[0].Key);
        // An authoring without SEO values keeps the SEO block absent.
        Assert.Null(found.Value.Seo);
    }

    private static UpdateSiteSettingsCommand Command(
        IReadOnlyList<SiteSettingsNavbarLinkInput> links,
        SiteSettingsSeoInput? seo = null) =>
        new(links, seo);

    private static IReadOnlyList<SiteSettingsNavbarLinkInput> Links(
        params (string Key, string ArabicLabel, string EnglishLabel, string Url)[] links) =>
        links
            .Select(link => new SiteSettingsNavbarLinkInput(
                link.Key,
                link.ArabicLabel,
                link.EnglishLabel,
                link.Url))
            .ToArray();

    private static SiteSettingsSeoInput Seo(
        string? arabicTitle = null,
        string? englishTitle = null,
        string? arabicDescription = null,
        string? englishDescription = null) =>
        new(arabicTitle, englishTitle, arabicDescription, englishDescription);

    private static CmsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
