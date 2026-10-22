using System.Text;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;
using Sanad.Modules.Cms.Application.Landing;
using Sanad.Modules.Cms.Domain.Landing;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class LandingAdminTests
{
    [Fact]
    public async Task AdminList_IncludesDrafts_Filtered_Paged()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection hero = DraftOf(LandingSectionType.Hero, "بطل", "Hero", displayOrder: 1);
        LandingSection about = DraftOf(LandingSectionType.About, "من نحن", "About", displayOrder: 0);
        about.Publish();
        db.LandingSections.AddRange(hero, about);
        await db.SaveChangesAsync();

        var all = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(), CancellationToken.None);
        Assert.True(all.IsSuccess);
        Assert.Equal(2, all.Value.TotalCount);

        var drafts = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(Status: LandingSectionStatus.Draft), CancellationToken.None);
        Assert.True(drafts.IsSuccess);
        var draft = Assert.Single(drafts.Value.Items);
        Assert.Equal("hero", draft.Type);

        var page = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(Page: 2, PageSize: 1), CancellationToken.None);
        Assert.True(page.IsSuccess);
        Assert.Equal(2, page.Value.TotalCount);
        Assert.Single(page.Value.Items);
    }

    [Fact]
    public async Task Create_PartialDraft_PublishRequiresCompleteness()
    {
        await using CmsDbContext db = CreateCmsContext();

        var created = await new CreateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new CreateLandingSectionCommand(
                LandingSectionType.Footer, null, null, null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(created.IsSuccess);
        Assert.Equal("Draft", created.Value.Status);

        var incomplete = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(created.Value.Id, ActorId), CancellationToken.None);
        Assert.True(incomplete.IsFailure);
        Assert.Equal("Cms.LandingSection.Incomplete", incomplete.Error.Code);

        var hero = await new CreateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new CreateLandingSectionCommand(
                LandingSectionType.Hero, "بطل", "Hero", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(hero.IsSuccess);

        var heroIncomplete = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(hero.Value.Id, ActorId), CancellationToken.None);
        Assert.True(heroIncomplete.IsFailure);
        Assert.Contains("cover", heroIncomplete.Error.Message, StringComparison.OrdinalIgnoreCase);

        var missing = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(Guid.NewGuid(), ActorId), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("Cms.LandingSection.NotFound", missing.Error.Code);
    }

    [Fact]
    public async Task Put_OnPublished_CreatesTwin_ThenUpdatesTwin()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection about = DraftOf(LandingSectionType.About, "من نحن", "About");
        about.Publish();
        db.LandingSections.Add(about);
        await db.SaveChangesAsync();

        var first = await new UpdateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new UpdateLandingSectionCommand(
                about.Id.Value, "من نحن 2", "About 2", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.NotEqual(about.Id.Value, first.Value.Id);
        Assert.Equal("Draft", first.Value.Status);
        Assert.Equal(about.Id.Value, first.Value.SourceId);

        var second = await new UpdateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new UpdateLandingSectionCommand(
                about.Id.Value, "من نحن 3", "About 3", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.Id, second.Value.Id);
        Assert.Equal("About 3", second.Value.EnglishTitle);
    }

    [Fact]
    public async Task Publish_Twin_ArchivesPreviousPublished()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection live = DraftOf(LandingSectionType.Footer, "قديم", "Old");
        live.Publish();
        LandingSection twin = LandingSection.Create(
            LandingSectionType.Footer, "جديد", "New", sourceId: live.Id);
        db.LandingSections.AddRange(live, twin);
        await db.SaveChangesAsync();

        var published = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(twin.Id.Value, ActorId), CancellationToken.None);
        Assert.True(published.IsSuccess);

        var rows = await db.LandingSections.AsNoTracking().ToListAsync();
        Assert.Equal(LandingSectionStatus.Archived, rows.Single(row => row.Id == live.Id).Status);
        Assert.Equal(LandingSectionStatus.Published, rows.Single(row => row.Id == twin.Id).Status);

        var reads = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);
        Assert.True(reads.IsSuccess);
        var single = Assert.Single(reads.Value);
        Assert.Equal("New", single.EnglishTitle);
    }

    [Fact]
    public async Task Delete_DraftOnly_PublishedHistoryRetained()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection draft = DraftOf(LandingSectionType.Steps, "مسودة", "Draft");
        LandingSection live = DraftOf(LandingSectionType.Faq, "سؤال", "Faq");
        live.Publish();
        db.LandingSections.AddRange(draft, live);
        await db.SaveChangesAsync();

        var deleted = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(draft.Id.Value, ActorId), CancellationToken.None);
        Assert.True(deleted.IsSuccess);

        var published = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(live.Id.Value, ActorId), CancellationToken.None);
        Assert.True(published.IsFailure);
        Assert.Equal("Cms.LandingSection.PublishedHistory", published.Error.Code);

        var missing = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(Guid.NewGuid(), ActorId), CancellationToken.None);
        Assert.True(missing.IsFailure);
        Assert.Equal("Cms.LandingSection.NotFound", missing.Error.Code);
    }

    [Fact]
    public async Task Visibility_Toggle_Persisted()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection live = DraftOf(LandingSectionType.Steps, "خطوات", "Steps");
        live.Publish();
        db.LandingSections.Add(live);
        await db.SaveChangesAsync();

        var hidden = await new SetLandingSectionVisibilityCommandHandler(db).Handle(
            new SetLandingSectionVisibilityCommand(live.Id.Value, false, ActorId), CancellationToken.None);
        Assert.True(hidden.IsSuccess);

        var reads = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);
        Assert.True(reads.IsSuccess);
        Assert.Empty(reads.Value);

        var missing = await new SetLandingSectionVisibilityCommandHandler(db).Handle(
            new SetLandingSectionVisibilityCommand(Guid.NewGuid(), true, ActorId), CancellationToken.None);
        Assert.True(missing.IsFailure);
    }

    [Fact]
    public async Task Reorder_Deterministic_RejectsDuplicatesAndUnknown()
    {
        await using CmsDbContext db = CreateCmsContext();

        LandingSection first = DraftOf(LandingSectionType.Hero, "بطل", "Hero", displayOrder: 0);
        first.Publish();
        LandingSection second = DraftOf(LandingSectionType.About, "من نحن", "About", displayOrder: 1);
        second.Publish();
        db.LandingSections.AddRange(first, second);
        await db.SaveChangesAsync();

        var reordered = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([
                new LandingReorderItem(first.Id.Value, 5),
                new LandingReorderItem(second.Id.Value, 2)
            ]),
            CancellationToken.None);
        Assert.True(reordered.IsSuccess);

        var reads = await new GetPublishedLandingSectionsQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetPublishedLandingSectionsQuery(), CancellationToken.None);
        Assert.True(reads.IsSuccess);
        Assert.Equal("about", reads.Value[0].Type);
        Assert.Equal("hero", reads.Value[1].Type);

        var duplicate = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([
                new LandingReorderItem(first.Id.Value, 0),
                new LandingReorderItem(first.Id.Value, 1)
            ]),
            CancellationToken.None);
        Assert.True(duplicate.IsFailure);
        Assert.Equal("Cms.Landing.ReorderInvalid", duplicate.Error.Code);

        var unknown = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([new LandingReorderItem(Guid.NewGuid(), 0)]),
            CancellationToken.None);
        Assert.True(unknown.IsFailure);
        Assert.Equal("Cms.Landing.ReorderInvalid", unknown.Error.Code);
    }

    [Fact]
    public async Task AdminList_TranslatesOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        LandingSection about = DraftOf(LandingSectionType.About, "من نحن", "About");
        about.Publish();
        db.LandingSections.Add(about);
        await db.SaveChangesAsync();

        var result = await new GetLandingAdminListQueryHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new GetLandingAdminListQuery(), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.TotalCount);
    }

    [Fact]
    public async Task AdminCommands_TranslateOnRelationalStore()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        await using var db = new CmsDbContext(
            new DbContextOptionsBuilder<CmsDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();

        var created = await new CreateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new CreateLandingSectionCommand(
                LandingSectionType.Footer, "تذييل", "Footer", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(created.IsSuccess);

        var published = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(created.Value.Id, ActorId), CancellationToken.None);
        Assert.True(published.IsSuccess);

        var twin = await new UpdateLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new UpdateLandingSectionCommand(
                created.Value.Id, "تذييل 2", "Footer 2", null, null, null, null, 0, true),
            CancellationToken.None);
        Assert.True(twin.IsSuccess);
        Assert.Equal("Draft", twin.Value.Status);

        var republished = await new PublishLandingSectionCommandHandler(db, CreateFamiliesContext(), CreateCaregiversContext()).Handle(
            new PublishLandingSectionCommand(twin.Value.Id, ActorId), CancellationToken.None);
        Assert.True(republished.IsSuccess);

        var deleted = await new DeleteLandingSectionCommandHandler(db).Handle(
            new DeleteLandingSectionCommand(created.Value.Id, ActorId), CancellationToken.None);
        Assert.True(deleted.IsFailure);

        var reordered = await new ReorderLandingSectionsCommandHandler(db).Handle(
            new ReorderLandingSectionsCommand([new LandingReorderItem(twin.Value.Id, 3)]),
            CancellationToken.None);
        Assert.True(reordered.IsSuccess);
    }

    // ---------------------- Landing media upload wiring ----------------------
    // The endpoint contract: the declared image/svg+xml branch must run the
    // sanitizer and store its output. These call the action directly, so the
    // CmsContent policy filter does not run here (it is asserted separately by
    // the route/authorization tests); what they prove is the controller →
    // sanitizer → storage wiring, which a refactor that forwarded the raw
    // stream on the SVG branch would silently break.

    [Fact]
    public async Task UploadMedia_StoresSanitizedBytes_NotTheRawSvg()
    {
        string storageRoot = CreateUploadRoot();

        try
        {
            AdminLandingController controller = CreateUploadController(storageRoot);

            byte[] payload = Encoding.UTF8.GetBytes(
                "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert('x')\">" +
                "<script>alert('y')</script><text x=\"1\" y=\"2\">hi</text></svg>");
            var file = new FormFile(new MemoryStream(payload), 0, payload.Length, "file", "art.svg")
            {
                Headers = new HeaderDictionary { ["Content-Type"] = "image/svg+xml" }
            };

            var result = await controller.UploadMedia(file, CancellationToken.None);

            // Documented success shape: 201 with the storage key.
            ObjectResult created = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status201Created, created.StatusCode);

            var response = Assert.IsType<LandingMediaUploadResponse>(created.Value);
            Assert.StartsWith("landing/", response.Key);
            Assert.EndsWith(".svg", response.Key);

            string storedPath = Path.Combine(
                storageRoot,
                response.Key.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(storedPath));

            // The bytes on disk are the sanitizer's output, not the upload.
            string stored = await File.ReadAllTextAsync(storedPath);
            Assert.DoesNotContain("<script", stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("alert", stored, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onload", stored, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<text", stored, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    [Fact]
    public async Task UploadMedia_RejectsDeeplyNestedSvgWithoutStoringAnything()
    {
        string storageRoot = CreateUploadRoot();

        try
        {
            AdminLandingController controller = CreateUploadController(storageRoot);

            byte[] payload = Encoding.UTF8.GetBytes(NestedSvg(50_000));
            var file = new FormFile(new MemoryStream(payload), 0, payload.Length, "file", "deep.svg")
            {
                Headers = new HeaderDictionary { ["Content-Type"] = "image/svg+xml" }
            };

            var result = await controller.UploadMedia(file, CancellationToken.None);

            // A clean 400 for the pathological upload: the request fails, the
            // process survives, and nothing reaches the store.
            ObjectResult rejected = Assert.IsType<ObjectResult>(result);
            Assert.Equal(StatusCodes.Status400BadRequest, rejected.StatusCode);
            Assert.Empty(Directory.GetFiles(storageRoot, "*", SearchOption.AllDirectories));
        }
        finally
        {
            Directory.Delete(storageRoot, recursive: true);
        }
    }

    // ------------------------- Landing SVG sanitizer -------------------------
    // The sanitizer is the only thing that may turn an uploaded SVG into stored
    // bytes. Each dangerous construct is proven removed (or the whole payload
    // rejected when it cannot be parsed safely), and safe markup is proven kept.

    [Fact]
    public void Svg_Sanitizer_StripsScriptElements()
    {
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert('x')</script><circle cx=\"1\" cy=\"1\" r=\"1\"/></svg>",
            keeps: ["<circle"],
            removes: ["<script", "alert"]);
    }

    [Fact]
    public void Svg_Sanitizer_StripsForeignObject()
    {
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><foreignObject width=\"10\" height=\"10\"><div xmlns=\"http://www.w3.org/1999/xhtml\">x</div></foreignObject><rect width=\"5\" height=\"5\"/></svg>",
            keeps: ["<rect"],
            removes: ["foreignObject", "<div"]);
    }

    [Fact]
    public void Svg_Sanitizer_StripsEventHandlerAttributes()
    {
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert('x')\"><rect onclick=\"evil()\" fill=\"red\" width=\"5\" height=\"5\"/></svg>",
            keeps: ["fill=\"red\""],
            removes: ["onload", "onclick", "alert", "evil"]);
    }

    [Fact]
    public void Svg_Sanitizer_StripsExternalReferences()
    {
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><style>@import url(\"https://evil.example/x.css\");</style><use xlink:href=\"#icon\"/><image href=\"https://evil.example/x.png\"/><a href=\"javascript:alert('x')\"><text>go</text></a></svg>",
            keeps: ["#icon"],
            removes: ["evil.example", "javascript:", "@import", "<style"]);
    }

    [Theory]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY xxe SYSTEM \"file:///etc/passwd\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&xxe;</text></svg>")]
    [InlineData("<?xml version=\"1.0\"?><!DOCTYPE svg [<!ENTITY a \"aaaa\"><!ENTITY b \"&a;&a;&a;&a;\">]><svg xmlns=\"http://www.w3.org/2000/svg\"><text>&b;</text></svg>")]
    public void Svg_Sanitizer_RejectsDtdAndEntityPayloads(string payload)
    {
        // A DTD is the only way to declare entities, and DTD processing is
        // prohibited, so XXE and entity expansion fail to parse and the payload
        // is rejected instead of being filtered.
        Assert.False(LandingSvgSanitizer.TrySanitize(Encoding.UTF8.GetBytes(payload), out byte[] sanitized));
        Assert.Empty(sanitized);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect></svg>")]
    [InlineData("<html><body>hi</body></html>")]
    [InlineData("not xml at all")]
    [InlineData("")]
    public void Svg_Sanitizer_RejectsUnparseableOrNonSvgPayloads(string payload)
    {
        Assert.False(LandingSvgSanitizer.TrySanitize(Encoding.UTF8.GetBytes(payload), out byte[] sanitized));
        Assert.Empty(sanitized);
    }

    [Fact]
    public void Svg_Sanitizer_KeepsSafeMarkup()
    {
        string payload =
            "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 10 10\">" +
            "<defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ffffff\"/></linearGradient></defs>" +
            "<rect width=\"10\" height=\"10\" fill=\"url(#g)\"/><circle cx=\"5\" cy=\"5\" r=\"2\"/></svg>";

        Assert.True(LandingSvgSanitizer.TrySanitize(Encoding.UTF8.GetBytes(payload), out byte[] sanitized));

        string text = Encoding.UTF8.GetString(sanitized);
        Assert.Contains("<linearGradient", text, StringComparison.Ordinal);
        Assert.Contains("url(#g)", text, StringComparison.Ordinal);
        Assert.Contains("<rect", text, StringComparison.Ordinal);
        Assert.Contains("<circle", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Svg_Sanitizer_RejectsDeeplyNestedDocument()
    {
        // 50 000 levels is ~400 KB of markup: far below the character ceiling,
        // so only the depth bound can stop it. Without that bound the recursive
        // walk overflows the stack, and .NET cannot catch a
        // StackOverflowException — the test host would die instead of failing.
        string payload = NestedSvg(50_000);

        Assert.False(LandingSvgSanitizer.TrySanitize(Encoding.UTF8.GetBytes(payload), out byte[] sanitized));
        Assert.Empty(sanitized);
    }

    [Fact]
    public void Svg_Sanitizer_AcceptsTheDepthLimitAndRejectsOnePastIt()
    {
        // The svg root counts as level 1, so MaxNestingDepth - 1 nested groups
        // sit exactly on the bound.
        string atLimit = NestedSvg(LandingSvgSanitizer.MaxNestingDepth - 1);
        Assert.True(LandingSvgSanitizer.TrySanitize(Encoding.UTF8.GetBytes(atLimit), out byte[] accepted));
        Assert.NotEmpty(accepted);

        string pastLimit = NestedSvg(LandingSvgSanitizer.MaxNestingDepth);
        Assert.False(LandingSvgSanitizer.TrySanitize(Encoding.UTF8.GetBytes(pastLimit), out byte[] rejected));
        Assert.Empty(rejected);
    }

    [Theory]
    [InlineData("iframe")]
    [InlineData("embed")]
    [InlineData("object")]
    [InlineData("set")]
    [InlineData("animate")]
    [InlineData("animateTransform")]
    [InlineData("animateMotion")]
    [InlineData("discard")]
    public void Svg_Sanitizer_StripsForbiddenElements(string element)
    {
        AssertSanitized(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\"><{element} attributeName=\"xlink:href\" to=\"javascript:alert(1)\"/><rect width=\"5\" height=\"5\"/></svg>",
            keeps: ["<rect"],
            removes: [$"<{element}", "javascript:", "alert"]);
    }

    [Fact]
    public void Svg_Sanitizer_StripsCaseVariedScriptElement()
    {
        // Element matching is on the local name, case-insensitively.
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><SCRIPT>alert('x')</SCRIPT><rect width=\"5\" height=\"5\"/></svg>",
            keeps: ["<rect"],
            removes: ["script", "alert"]);
    }

    [Fact]
    public void Svg_Sanitizer_StripsNamespacePrefixedScriptElement()
    {
        // A prefixed name in another namespace is not a way around the rule.
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><html:script xmlns:html=\"http://www.w3.org/1999/xhtml\">alert('x')</html:script><rect width=\"5\" height=\"5\"/></svg>",
            keeps: ["<rect"],
            removes: ["script", "alert"]);
    }

    [Fact]
    public void Svg_Sanitizer_StripsCommentsAndCdataPayloads()
    {
        // Neither a comment hiding markup nor CDATA inside a forbidden element
        // may survive into the stored bytes.
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><!-- <script>alert(1)</script> --><script><![CDATA[alert(2)]]></script><rect width=\"5\" height=\"5\"/></svg>",
            keeps: ["<rect"],
            removes: ["<script", "alert", "<!--"]);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"5\" height=\"5\" filter=\"url(https://evil.example/f.svg#x)\"/></svg>", "filter")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"5\" height=\"5\" style=\"background:url('https://evil.example/a.png')\"/></svg>", "style")]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><rect width=\"5\" height=\"5\" fill=\"url(https://evil.example\"/></svg>", "fill")]
    public void Svg_Sanitizer_StripsExternalUrlReferences(string payload, string removedAttribute)
    {
        // url() outside href/src (style and presentation attributes) is an
        // egress/tracking channel; an unterminated url( is treated as unsafe.
        AssertSanitized(
            payload,
            keeps: ["<rect", "width=\"5\""],
            removes: ["evil.example", removedAttribute]);
    }

    [Fact]
    public void Svg_Sanitizer_KeepsLocalUrlReferences()
    {
        // Same-document url(#...) targets are legitimate and must survive, with
        // or without quotes.
        AssertSanitized(
            "<svg xmlns=\"http://www.w3.org/2000/svg\"><defs><linearGradient id=\"g\"><stop offset=\"0\" stop-color=\"#ffffff\"/></linearGradient></defs><rect width=\"5\" height=\"5\" fill=\"url(#g)\" style=\"stroke:url( '#g' )\"/></svg>",
            keeps: ["<linearGradient", "fill=\"url(#g)\"", "stroke:url( '#g' )"],
            removes: []);
    }

    private static void AssertSanitized(string payload, string[] keeps, string[] removes)
    {
        Assert.True(LandingSvgSanitizer.TrySanitize(Encoding.UTF8.GetBytes(payload), out byte[] sanitized));

        // The stored bytes are the sanitized serialization, never the raw upload.
        string text = Encoding.UTF8.GetString(sanitized);
        Assert.NotEqual(payload, text);

        foreach (string expected in keeps)
            Assert.Contains(expected, text, StringComparison.Ordinal);

        foreach (string forbidden in removes)
            Assert.DoesNotContain(forbidden, text, StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Guid ActorId = Guid.NewGuid();

    private static LandingSection DraftOf(
        LandingSectionType type, string arabicTitle, string englishTitle, int displayOrder = 0) =>
        LandingSection.Create(type, arabicTitle, englishTitle, displayOrder: displayOrder);

    private static CmsDbContext CreateCmsContext()
    {
        var options = new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CmsDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static FamiliesDbContext CreateFamiliesContext()
    {
        var options = new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new FamiliesDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static CaregiversDbContext CreateCaregiversContext()
    {
        var options = new DbContextOptionsBuilder<CaregiversDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CaregiversDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    /// <summary>svg root plus <paramref name="nesting"/> nested g elements.</summary>
    private static string NestedSvg(int nesting) =>
        "<svg xmlns=\"http://www.w3.org/2000/svg\">" +
        string.Concat(Enumerable.Repeat("<g>", nesting)) +
        "<rect width=\"1\" height=\"1\"/>" +
        string.Concat(Enumerable.Repeat("</g>", nesting)) +
        "</svg>";

    private static string CreateUploadRoot()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "sanad-landing-upload-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static AdminLandingController CreateUploadController(string storageRoot)
    {
        IOptions<LocalStorageOptions> storageOptions =
            Options.Create(new LocalStorageOptions { RootPath = storageRoot });
        var storage = new LocalDiskFileStorage(storageOptions);

        return new AdminLandingController(new NoOpSender(), storage, storageOptions)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    private sealed class NoOpSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
