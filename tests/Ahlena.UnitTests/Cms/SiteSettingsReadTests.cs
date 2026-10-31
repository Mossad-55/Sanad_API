using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Ahlena.Modules.Cms.Application.Settings;
using Ahlena.Modules.Cms.Domain.Settings;
using Ahlena.Modules.Cms.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Cms;

/// <summary>
/// The public read of the published site settings. The tests author through the
/// real command handler and then read through the real public handler, so the
/// empty-store behaviour, the ordering and the exposed fields are the real
/// behaviour of the stored row.
/// </summary>
public sealed class SiteSettingsReadTests
{
    [Fact]
    public async Task Public_read_is_honest_and_empty_before_anything_is_authored()
    {
        await using CmsDbContext db = CreateContext();

        var result = await new GetPublicSiteSettingsQueryHandler(db)
            .Handle(new GetPublicSiteSettingsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.NavbarLinks);
        Assert.Null(result.Value.Seo);
    }

    [Fact]
    public async Task Public_read_returns_the_authored_links_in_order_and_the_seo_defaults()
    {
        await using CmsDbContext db = CreateContext();

        var written = await new UpdateSiteSettingsCommandHandler(db).Handle(
            new UpdateSiteSettingsCommand(
                [
                    new SiteSettingsNavbarLinkInput("library", "المكتبة", "Library", "/library"),
                    new SiteSettingsNavbarLinkInput("help", "المساعدة", "Help", "/help"),
                    new SiteSettingsNavbarLinkInput("about", "من نحن", "About", "https://ahlena.example/about")
                ],
                new SiteSettingsSeoInput("أهلنا", "Ahlena", "وصف", "Description")),
            default);
        Assert.True(written.IsSuccess);

        var result = await new GetPublicSiteSettingsQueryHandler(db)
            .Handle(new GetPublicSiteSettingsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.NavbarLinks.Count);
        Assert.Equal(["library", "help", "about"], result.Value.NavbarLinks.Select(link => link.Key));
        Assert.Equal("المكتبة", result.Value.NavbarLinks[0].ArabicLabel);
        Assert.Equal("Library", result.Value.NavbarLinks[0].EnglishLabel);
        Assert.Equal("/library", result.Value.NavbarLinks[0].Url);
        Assert.Equal("https://ahlena.example/about", result.Value.NavbarLinks[2].Url);

        Assert.NotNull(result.Value.Seo);
        Assert.Equal("أهلنا", result.Value.Seo!.ArabicTitle);
        Assert.Equal("Ahlena", result.Value.Seo.EnglishTitle);
        Assert.Equal("وصف", result.Value.Seo.ArabicDescription);
        Assert.Equal("Description", result.Value.Seo.EnglishDescription);
    }

    [Fact]
    public async Task Public_read_keeps_the_seo_block_absent_when_only_links_were_authored()
    {
        await using CmsDbContext db = CreateContext();

        var written = await new UpdateSiteSettingsCommandHandler(db).Handle(
            new UpdateSiteSettingsCommand(
                [
                    new SiteSettingsNavbarLinkInput("library", "المكتبة", "Library", "/library")
                ],
                null),
            default);
        Assert.True(written.IsSuccess);

        var result = await new GetPublicSiteSettingsQueryHandler(db)
            .Handle(new GetPublicSiteSettingsQuery(), default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.NavbarLinks);
        Assert.Null(result.Value.Seo);
    }

    [Fact]
    public void Public_projection_exposes_only_the_intended_published_fields()
    {
        string[] responseMembers = typeof(PublicSiteSettingsResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["NavbarLinks", "Seo"], responseMembers);

        string[] linkMembers = typeof(SiteSettingsNavbarLink)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["ArabicLabel", "EnglishLabel", "Key", "Url"], linkMembers);

        string[] seoMembers = typeof(SiteSettingsSeoResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["ArabicDescription", "ArabicTitle", "EnglishDescription", "EnglishTitle"], seoMembers);

        // The admin read carries the authoring timestamps; the public read
        // never exposes them.
        string[] adminMembers = typeof(SiteSettingsAdminResponse)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(["CreatedOnUtc", "NavbarLinks", "Seo", "UpdatedOnUtc"], adminMembers);
    }

    [Fact]
    public async Task The_single_row_keeps_its_fixed_identity_across_writes()
    {
        await using CmsDbContext db = CreateContext();
        var handler = new UpdateSiteSettingsCommandHandler(db);

        await handler.Handle(
            new UpdateSiteSettingsCommand(
                [new SiteSettingsNavbarLinkInput("library", "المكتبة", "Library", "/library")],
                null),
            default);
        await handler.Handle(
            new UpdateSiteSettingsCommand(
                [new SiteSettingsNavbarLinkInput("help", "المساعدة", "Help", "/help")],
                null),
            default);

        List<SiteSettings> rows = await db.SiteSettings.ToListAsync();
        SiteSettings single = Assert.Single(rows);
        Assert.Equal(SiteSettingsId.Singleton, single.Id);
        Assert.Equal(SiteSettingsId.SingletonKey, single.Id.Value);
    }

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
