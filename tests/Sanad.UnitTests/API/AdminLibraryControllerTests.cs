using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.Modules.Cms.Application.Library;
using Sanad.Modules.Cms.Domain.Library;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.API;

public sealed class AdminLibraryControllerTests
{
    private const string CamelCaseSections =
        "[{\"displayOrder\":1,\"arabicTitle\":\"الأول\",\"englishTitle\":\"First\",\"arabicBullets\":[\"نقطة\"],\"englishBullets\":[\"bullet\"]}," +
        "{\"displayOrder\":2,\"arabicTitle\":\"الثاني\",\"englishTitle\":\"Second\",\"arabicBullets\":[],\"englishBullets\":[]} ]";

    private const string PascalCaseSections =
        "[{\"DisplayOrder\":1,\"ArabicTitle\":\"الأول\",\"EnglishTitle\":\"First\",\"ArabicBullets\":[\"نقطة\"],\"EnglishBullets\":[\"bullet\"]}," +
        "{\"DisplayOrder\":2,\"ArabicTitle\":\"الثاني\",\"EnglishTitle\":\"Second\",\"ArabicBullets\":[],\"EnglishBullets\":[]} ]";

    [Fact]
    public async Task Create_CamelCaseAndPascalCase_ProduceEquivalentSections()
    {
        await using CmsDbContext camelDb = CreateDb();
        var camelStorage = new RecordingStorage();
        var camelController = CreateController(camelDb, camelStorage, "camel-trace");
        IActionResult camelResult = await camelController.CreateContent(
            ArticleRequest("Article", null, CamelCaseSections), PngFile(), CancellationToken.None);
        Assert.Equal(201, Assert.IsType<ObjectResult>(camelResult).StatusCode);
        Assert.Equal(1, camelStorage.SaveCount);

        await using CmsDbContext pascalDb = CreateDb();
        var pascalController = CreateController(pascalDb, new RecordingStorage(), "pascal-trace");
        IActionResult pascalResult = await pascalController.CreateContent(
            ArticleRequest("Article", null, PascalCaseSections), PngFile(), CancellationToken.None);
        Assert.Equal(201, Assert.IsType<ObjectResult>(pascalResult).StatusCode);

        List<LibraryContent> camel = await ReadAllAsync(camelDb);
        List<LibraryContent> pascal = await ReadAllAsync(pascalDb);
        LibraryContent camelContent = Assert.Single(camel);
        LibraryContent pascalContent = Assert.Single(pascal);
        Assert.Equal("Draft", camelContent.Status.ToString());
        Assert.Equal(
            pascalContent.Sections.OrderBy(x => x.DisplayOrder).Select(x => (x.DisplayOrder, x.ArabicTitle, x.EnglishTitle)),
            camelContent.Sections.OrderBy(x => x.DisplayOrder).Select(x => (x.DisplayOrder, x.ArabicTitle, x.EnglishTitle)));
        Assert.Equal(["نقطة"], camelContent.Sections.Single(x => x.DisplayOrder == 1).ArabicBullets);
        Assert.Empty(camelContent.Sections.Single(x => x.DisplayOrder == 2).ArabicBullets);
    }

    [Fact]
    public async Task Create_ValidVideo_Returns201()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "video-trace");

        IActionResult result = await controller.CreateContent(
            VideoRequest("https://youtu.be/abc123", 300, null), PngFile(), CancellationToken.None);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        LibraryContent content = Assert.Single(await ReadAllAsync(db));
        Assert.Equal("https://youtu.be/abc123", content.VideoUrl);
        Assert.Equal(300, content.DurationSeconds);
        Assert.Empty(content.Sections);
    }

    [Fact]
    public async Task CanonicalPostmanArticleExample_Returns201()
    {
        await using CmsDbContext db = CreateDb();
        LibraryCategory category = LibraryCategory.Create("صحة", "Health", true, DateTime.UtcNow);
        db.LibraryCategories.Add(category);
        await db.SaveChangesAsync();
        Assert.True((await db.LibraryCategories.AsNoTracking().SingleAsync()).IsActive);
        var controller = CreateController(db, new RecordingStorage(), "canonical-article");
        // Literal documented/Postman form values; replace only the category/file placeholders.
        var request = new CreateLibraryContentRequest(
            "Article", category.Id.Value.ToString(), "نصيحة صحية", "Healthy tip", "ملخص قصير", "Short summary",
            null, null, false,
            "[{\"displayOrder\":1,\"arabicTitle\":\"الأول\",\"englishTitle\":\"First\",\"arabicBullets\":[\"نقطة\"],\"englishBullets\":[\"bullet\"]}]");

        Assert.Equal(201, Assert.IsType<ObjectResult>(
            await controller.CreateContent(request, PngFile(), CancellationToken.None)).StatusCode);
        LibraryContent stored = Assert.Single(await ReadAllAsync(db));
        Assert.Equal("Healthy tip", stored.EnglishTitle);
        Assert.Equal(category.Id, stored.CategoryId);
        Assert.Equal("bullet", Assert.Single(Assert.Single(stored.Sections).EnglishBullets));
    }

    [Fact]
    public async Task CanonicalPostmanVideoUpdateExample_Returns200()
    {
        await using CmsDbContext db = CreateDb();
        LibraryCategory category = LibraryCategory.Create("صحة", "Health", true, DateTime.UtcNow);
        db.LibraryCategories.Add(category);
        await db.SaveChangesAsync();
        var seeded = await new CreateLibraryContentCommandHandler(db).Handle(
            new CreateLibraryContentCommand(LibraryContentType.Video, category.Id.Value,
                "فيديو", "Video", "ملخص", "Summary", "https://youtu.be/old", 60,
                "library/old.png", false, null), CancellationToken.None);
        Assert.True(seeded.IsSuccess);
        LibraryContent ready = await ReadAsync(db, seeded.Value.Id);
        Assert.Equal(LibraryContentType.Video, ready.Type);
        Assert.Equal(LibraryContentStatus.Draft, ready.Status);
        Assert.True((await db.LibraryCategories.AsNoTracking().SingleAsync()).IsActive);
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "canonical-video");
        var request = new UpdateLibraryContentRequest(
            "Video", category.Id.Value.ToString(), "فيديو جديد", "New video", "ملخص قصير", "Short summary",
            "https://youtu.be/abc123", 300, false, null);

        Assert.IsType<OkObjectResult>(await controller.UpdateContent(ready.Id.Value, request, null, CancellationToken.None));
        LibraryContent stored = await ReadAsync(db, ready.Id.Value);
        Assert.Equal("New video", stored.EnglishTitle);
        Assert.Equal("https://youtu.be/abc123", stored.VideoUrl);
        Assert.Equal(300, stored.DurationSeconds);
        Assert.Equal("library/old.png", stored.CoverImagePath);
        Assert.Equal(0, storage.SaveCount);
    }

    [Fact]
    public async Task Create_NullSectionEntry_Returns400WithoutThrowing()
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "null-entry-trace");

        IActionResult result = await controller.CreateContent(
            ArticleRequest("Article", null, "[null]"), PngFile(), CancellationToken.None);

        ProblemDetails problem = AssertProblem(result, 400, "Cms.Library.InvalidSections", "null-entry-trace");
        Assert.NotNull(problem.Detail);
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"displayOrder\":1}")]
    [InlineData("\"just-a-string\"")]
    [InlineData("123")]
    [InlineData("[{\"displayOrder\":\"1\",\"arabicTitle\":\"أ\",\"englishTitle\":\"E\",\"arabicBullets\":[],\"englishBullets\":[]}]")]
    [InlineData("[{\"DisplayOrder\":\"1\",\"ArabicTitle\":\"أ\",\"EnglishTitle\":\"E\",\"ArabicBullets\":[],\"EnglishBullets\":[]}]")]
    [InlineData("[{\"displayOrder\":\"abc\",\"arabicTitle\":\"أ\",\"englishTitle\":\"E\",\"arabicBullets\":[],\"englishBullets\":[]}]")]
    public async Task Create_MalformedOrWrongShapeSections_Return400(string sectionsJson)
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "shape-trace");

        IActionResult result = await controller.CreateContent(
            ArticleRequest("Article", null, sectionsJson), PngFile(), CancellationToken.None);

        AssertProblem(result, 400, "Cms.Library.InvalidSections", "shape-trace");
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Theory]
    [InlineData("Magazine", "Cms.Library.InvalidType", "type")]
    [InlineData("", "Cms.Library.InvalidType", "type")]
    public async Task Create_InvalidType_Returns400WithFieldError(string type, string code, string field)
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "type-trace");

        IActionResult result = await controller.CreateContent(
            ArticleRequest(type, null, CamelCaseSections), PngFile(), CancellationToken.None);

        ProblemDetails problem = AssertProblem(result, 400, code, "type-trace");
        Assert.True(Errors(problem).ContainsKey(field));
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_MalformedCategory_Returns400WithFieldError()
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "category-trace");

        IActionResult result = await controller.CreateContent(
            ArticleRequest("Article", "not-a-guid", CamelCaseSections), PngFile(), CancellationToken.None);

        ProblemDetails problem = AssertProblem(result, 400, "Cms.Library.InvalidCategory", "category-trace");
        Assert.True(Errors(problem).ContainsKey("categoryId"));
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("[]")]
    public async Task Create_MissingOrEmptyArticleSections_ReturnFieldErrors(string? sectionsJson)
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "missing-sections-trace");

        IActionResult result = await controller.CreateContent(
            ArticleRequest("Article", null, sectionsJson), PngFile(), CancellationToken.None);

        ProblemDetails problem = AssertProblem(result, 400, "Cms.LibraryContent.Invalid", "missing-sections-trace");
        Assert.True(Errors(problem).ContainsKey("sectionsJson"));
        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_ZeroAndDuplicateDisplayOrders_ReturnFieldErrors()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "order-trace");
        string zero = "[{\"displayOrder\":0,\"arabicTitle\":\"أ\",\"englishTitle\":\"E\",\"arabicBullets\":[],\"englishBullets\":[]}]";
        string duplicate = "[{\"displayOrder\":1,\"arabicTitle\":\"أ\",\"englishTitle\":\"A\",\"arabicBullets\":[],\"englishBullets\":[]}," +
            "{\"displayOrder\":1,\"arabicTitle\":\"ب\",\"englishTitle\":\"B\",\"arabicBullets\":[],\"englishBullets\":[]}]";

        ProblemDetails zeroProblem = AssertProblem(
            await controller.CreateContent(ArticleRequest("Article", null, zero), PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "order-trace");
        Assert.True(Errors(zeroProblem).ContainsKey("sectionsJson[0].displayOrder"));

        ProblemDetails duplicateProblem = AssertProblem(
            await controller.CreateContent(ArticleRequest("Article", null, duplicate), PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "order-trace");
        Assert.True(Errors(duplicateProblem).ContainsKey("sectionsJson"));
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_TooManySections_ReturnFieldError()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "count-trace");
        string many = "[" + string.Join(",", Enumerable.Range(1, 41).Select(i =>
            "{\"displayOrder\":" + i + ",\"arabicTitle\":\"أ" + i + "\",\"englishTitle\":\"E" + i + "\",\"arabicBullets\":[],\"englishBullets\":[]}")) + "]";

        ProblemDetails problem = AssertProblem(
            await controller.CreateContent(ArticleRequest("Article", null, many), PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "count-trace");

        Assert.True(Errors(problem).ContainsKey("sectionsJson"));
        Assert.Empty(await ReadAllAsync(db));
    }

    [Theory]
    [InlineData(" ", "Title", "ملخص", "Summary", "arabicTitle")]
    [InlineData("Title", "", "ملخص", "Summary", "englishTitle")]
    [InlineData("Title", "Title", " ", "Summary", "arabicSummary")]
    public async Task Create_RequiredTextViolations_ReturnFieldErrors(
        string arabicTitle, string englishTitle, string arabicSummary, string englishSummary, string field)
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "text-trace");
        var request = new CreateLibraryContentRequest(
            "Article", null, arabicTitle, englishTitle, arabicSummary, englishSummary, null, null, false, CamelCaseSections);

        ProblemDetails problem = AssertProblem(
            await controller.CreateContent(request, PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "text-trace");

        Assert.True(Errors(problem).ContainsKey(field));
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_OverlongTitles_ReturnFieldErrors()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "overlong-trace");
        var request = new CreateLibraryContentRequest(
            "Article", null, new string('ع', 201), new string('e', 201), "ملخص", "Summary", null, null, false, CamelCaseSections);

        ProblemDetails problem = AssertProblem(
            await controller.CreateContent(request, PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "overlong-trace");

        var errors = Errors(problem);
        Assert.True(errors.ContainsKey("arabicTitle"));
        Assert.True(errors.ContainsKey("englishTitle"));
    }

    [Fact]
    public async Task Create_BulletViolations_ReturnIndexedFieldErrors()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "bullet-trace");
        string sections = "[{\"displayOrder\":1,\"arabicTitle\":\"أ\",\"englishTitle\":\"E\"," +
            "\"arabicBullets\":[\"ok\",\" \"],\"englishBullets\":[\"" + new string('e', 501) + "\"]}]";

        ProblemDetails problem = AssertProblem(
            await controller.CreateContent(ArticleRequest("Article", null, sections), PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "bullet-trace");

        var errors = Errors(problem);
        Assert.True(errors.ContainsKey("sectionsJson[0].arabicBullets[1]"));
        Assert.True(errors.ContainsKey("sectionsJson[0].englishBullets[0]"));
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_TooManyBullets_ReturnFieldError()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "bullets-count-trace");
        string bullets = "[" + string.Join(",", Enumerable.Repeat("\"b\"", 31)) + "]";
        string sections = "[{\"displayOrder\":1,\"arabicTitle\":\"أ\",\"englishTitle\":\"E\",\"arabicBullets\":" + bullets + ",\"englishBullets\":[]}]";

        ProblemDetails problem = AssertProblem(
            await controller.CreateContent(ArticleRequest("Article", null, sections), PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "bullets-count-trace");

        Assert.True(Errors(problem).ContainsKey("sectionsJson[0].arabicBullets"));
    }

    [Theory]
    [InlineData("not-a-url", 300, "videoUrl")]
    [InlineData("/watch?v=x", 300, "videoUrl")]
    [InlineData("https://youtu.be/abc123", null, "durationSeconds")]
    [InlineData("https://youtu.be/abc123", 0, "durationSeconds")]
    public async Task Create_VideoViolations_ReturnFieldErrors(string url, int? duration, string field)
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "video-trace");

        ProblemDetails problem = AssertProblem(
            await controller.CreateContent(VideoRequest(url, duration, null), PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "video-trace");

        Assert.True(Errors(problem).ContainsKey(field));
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_InvalidVideoDuration_ReturnsExamplePayloadShape()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "request-correlation-id");

        IActionResult result = await controller.CreateContent(
            VideoRequest("https://youtu.be/abc123", 0, null), PngFile(), CancellationToken.None);

        ProblemDetails problem = AssertProblem(result, 400, "Cms.LibraryContent.Invalid", "request-correlation-id");
        Assert.Equal("Bad Request", problem.Title);
        Assert.Equal("One or more library content fields are invalid.", problem.Detail);
        var errors = Errors(problem);
        Assert.True(errors.ContainsKey("durationSeconds"));
        Assert.NotEmpty(errors["durationSeconds"]);
    }

    [Fact]
    public async Task Create_MixedArticleVideoFields_ReturnFieldErrors()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "mixed-trace");

        var articleWithVideo = new CreateLibraryContentRequest(
            "Article", null, "عنوان", "Title", "ملخص", "Summary", "https://youtu.be/x", null, false, CamelCaseSections);
        ProblemDetails videoOnArticle = AssertProblem(
            await controller.CreateContent(articleWithVideo, PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "mixed-trace");
        Assert.True(Errors(videoOnArticle).ContainsKey("videoUrl"));

        var articleWithDuration = articleWithVideo with { VideoUrl = null, DurationSeconds = 10 };
        ProblemDetails durationOnArticle = AssertProblem(
            await controller.CreateContent(articleWithDuration, PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "mixed-trace");
        Assert.True(Errors(durationOnArticle).ContainsKey("durationSeconds"));

        var videoWithSections = new CreateLibraryContentRequest(
            "Video", null, "عنوان", "Title", "ملخص", "Summary", "https://youtu.be/x", 60, false, CamelCaseSections);
        ProblemDetails sectionsOnVideo = AssertProblem(
            await controller.CreateContent(videoWithSections, PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "mixed-trace");
        Assert.True(Errors(sectionsOnVideo).ContainsKey("sectionsJson"));
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_EmptyAndNullBullets_RemainValid()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "bullets-empty-trace");
        string sections = "[{\"displayOrder\":1,\"arabicTitle\":\"أ\",\"englishTitle\":\"E\",\"arabicBullets\":[],\"englishBullets\":null}]";

        IActionResult result = await controller.CreateContent(
            ArticleRequest("Article", null, sections), PngFile(), CancellationToken.None);

        Assert.Equal(201, Assert.IsType<ObjectResult>(result).StatusCode);
        LibraryContent content = Assert.Single(await ReadAllAsync(db));
        Assert.Empty(content.Sections.Single().ArabicBullets);
        Assert.Empty(content.Sections.Single().EnglishBullets);
    }

    [Fact]
    public async Task Update_OmittedSectionsAndFile_PreservesExisting()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent content = await CreateArticleAsync(db, null, CamelCaseSections, "library/old.png");
        var controller = CreateController(db, new RecordingStorage(), "omit-trace");

        IActionResult result = await controller.UpdateContent(
            content.Id.Value,
            new UpdateLibraryContentRequest("Article", null, "محدث", "Updated", "ملخص", "Summary", null, null, false, null),
            null,
            CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        LibraryContent reloaded = await ReadAsync(db, content.Id.Value);
        Assert.Equal("Updated", reloaded.EnglishTitle);
        Assert.Equal(2, reloaded.Sections.Count);
        Assert.Equal("library/old.png", reloaded.CoverImagePath);
    }

    [Fact]
    public async Task Update_ExplicitEmptyArticleSections_RejectsWithoutChangingState()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent content = await CreateArticleAsync(db, null, CamelCaseSections, "library/old.png");
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "empty-update-trace");

        IActionResult result = await controller.UpdateContent(
            content.Id.Value,
            new UpdateLibraryContentRequest("Article", null, "محدث", "Updated", "ملخص", "Summary", null, null, false, "[]"),
            PngFile(),
            CancellationToken.None);

        AssertProblem(result, 400, "Cms.LibraryContent.Invalid", "empty-update-trace");
        Assert.Equal(0, storage.SaveCount);
        LibraryContent reloaded = await ReadAsync(db, content.Id.Value);
        Assert.Equal(2, reloaded.Sections.Count);
        Assert.Equal("library/old.png", reloaded.CoverImagePath);
    }

    [Fact]
    public async Task Update_IgnoredRequestType_ValidatesAgainstPersistedType()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent content = await CreateArticleAsync(db, null, CamelCaseSections, "library/old.png");
        var controller = CreateController(db, new RecordingStorage(), "effective-type-trace");

        IActionResult valid = await controller.UpdateContent(
            content.Id.Value,
            new UpdateLibraryContentRequest("Video", null, "محدث", "Updated", "ملخص", "Summary", null, null, false, null),
            null,
            CancellationToken.None);
        Assert.IsType<OkObjectResult>(valid);
        Assert.Equal("Article", (await ReadAsync(db, content.Id.Value)).Type.ToString());

        IActionResult mixed = await controller.UpdateContent(
            content.Id.Value,
            new UpdateLibraryContentRequest("Video", null, "محدث", "Updated", "ملخص", "Summary", "https://youtu.be/x", 60, false, null),
            null,
            CancellationToken.None);
        ProblemDetails problem = AssertProblem(mixed, 400, "Cms.LibraryContent.Invalid", "effective-type-trace");
        Assert.True(Errors(problem).ContainsKey("videoUrl"));
    }

    [Fact]
    public async Task Update_NonDraftMissingAndCategoryFailures_PreserveOutcomes()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent draft = await CreateArticleAsync(db, null, CamelCaseSections, "library/old.png");
        LibraryContent published = await CreateArticleAsync(db, null, CamelCaseSections, "library/old.png");
        db.LibraryContents.Single(content => content.Id.Value == published.Id.Value).Publish();
        await db.SaveChangesAsync();
        LibraryCategory inactive = LibraryCategory.Create("قديم", "Old", false, DateTime.UtcNow);
        db.LibraryCategories.Add(inactive);
        await db.SaveChangesAsync();
        var controller = CreateController(db, new RecordingStorage(), "lifecycle-trace");
        UpdateLibraryContentRequest body(string? categoryId) => new("Article", categoryId, "محدث", "Updated", "ملخص", "Summary", null, null, false, null);

        AssertProblem(
            await controller.UpdateContent(published.Id.Value, body(null), null, CancellationToken.None),
            409, "Cms.LibraryContent.InvalidOperation", "lifecycle-trace");
        AssertProblem(
            await controller.UpdateContent(Guid.NewGuid(), body(null), null, CancellationToken.None),
            404, "Cms.LibraryContent.NotFound", "lifecycle-trace");
        AssertProblem(
            await controller.UpdateContent(draft.Id.Value, body(Guid.NewGuid().ToString()), null, CancellationToken.None),
            400, "Cms.Library.InvalidCategory", "lifecycle-trace");
        AssertProblem(
            await controller.UpdateContent(draft.Id.Value, body(inactive.Id.Value.ToString()), null, CancellationToken.None),
            409, "Cms.Library.CategoryInactive", "lifecycle-trace");
    }

    [Fact]
    public async Task Create_RejectedRequest_LeavesNoCoverOrRow()
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "no-cover-trace");
        string duplicate = "[{\"displayOrder\":1,\"arabicTitle\":\"أ\",\"englishTitle\":\"A\",\"arabicBullets\":[],\"englishBullets\":[]}," +
            "{\"displayOrder\":1,\"arabicTitle\":\"ب\",\"englishTitle\":\"B\",\"arabicBullets\":[],\"englishBullets\":[]}]";

        AssertProblem(
            await controller.CreateContent(ArticleRequest("Article", null, duplicate), PngFile(), CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "no-cover-trace");

        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(storage.DeletedKeys);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_HandlerFailureAfterUpload_DeletesOnlyNewCover()
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "cleanup-create-trace");

        AssertProblem(
            await controller.CreateContent(
                ArticleRequest("Article", Guid.NewGuid().ToString(), CamelCaseSections), PngFile(), CancellationToken.None),
            400, "Cms.Library.InvalidCategory", "cleanup-create-trace");

        string uploaded = Assert.Single(storage.SavedKeys);
        Assert.Equal([uploaded], storage.DeletedKeys);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Update_FailedValidation_KeepsOldCoverAndSections()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent content = await CreateArticleAsync(db, null, CamelCaseSections, "library/old.png");
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "keep-cover-trace");

        AssertProblem(
            await controller.UpdateContent(
                content.Id.Value,
                new UpdateLibraryContentRequest("Article", null, " ", "Updated", "ملخص", "Summary", null, null, false, CamelCaseSections),
                PngFile(),
                CancellationToken.None),
            400, "Cms.LibraryContent.Invalid", "keep-cover-trace");

        Assert.Equal(0, storage.SaveCount);
        LibraryContent reloaded = await ReadAsync(db, content.Id.Value);
        Assert.Equal(2, reloaded.Sections.Count);
        Assert.Equal("library/old.png", reloaded.CoverImagePath);
    }

    [Fact]
    public async Task Update_HandlerFailureAfterUpload_DeletesOnlyNewCover()
    {
        await using CmsDbContext db = CreateDb();
        LibraryContent content = await CreateArticleAsync(db, null, CamelCaseSections, "library/old.png");
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "cleanup-update-trace");

        AssertProblem(
            await controller.UpdateContent(
                content.Id.Value,
                new UpdateLibraryContentRequest("Article", Guid.NewGuid().ToString(), "محدث", "Updated", "ملخص", "Summary", null, null, false, null),
                PngFile(),
                CancellationToken.None),
            400, "Cms.Library.InvalidCategory", "cleanup-update-trace");

        string uploaded = Assert.Single(storage.SavedKeys);
        Assert.Equal([uploaded], storage.DeletedKeys);
        LibraryContent reloaded = await ReadAsync(db, content.Id.Value);
        Assert.Equal("library/old.png", reloaded.CoverImagePath);
        Assert.Equal(2, reloaded.Sections.Count);
    }

    [Fact]
    public async Task Create_StorageFailure_PreservesErrorCodeWithoutDispatch()
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage { FailSaveWith = StorageErrors.TooLarge };
        var sender = new DispatchingSender(db);
        var controller = new AdminLibraryController(sender, storage)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

        IActionResult result = await controller.CreateContent(
            ArticleRequest("Article", null, CamelCaseSections), PngFile(), CancellationToken.None);

        AssertProblem(result, 400, "Storage.File.TooLarge", string.Empty, checkTrace: false);
        Assert.Null(sender.LastRequest);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Create_SpoofedImageContentType_Returns400WithoutUpload()
    {
        await using CmsDbContext db = CreateDb();
        var storage = new RecordingStorage();
        var controller = CreateController(db, storage, "spoof-trace");
        var file = new FormFile(new MemoryStream([1, 2, 3]), 0, 3, "file", "cover.png");
        file.Headers = new HeaderDictionary { ["Content-Type"] = "image/png" };

        AssertProblem(
            await controller.CreateContent(ArticleRequest("Article", null, CamelCaseSections), file, CancellationToken.None),
            400, "Storage.File.UnsupportedType", "spoof-trace");

        Assert.Equal(0, storage.SaveCount);
        Assert.Empty(await ReadAllAsync(db));
    }

    [Fact]
    public async Task Failures_CarryNoRawParserOrDatabaseDetails()
    {
        await using CmsDbContext db = CreateDb();
        var controller = CreateController(db, new RecordingStorage(), "leak-trace");
        string[] invalidSections = ["not-json", "[null]"];

        foreach (string sections in invalidSections)
        {
            ProblemDetails problem = AssertProblem(
                await controller.CreateContent(ArticleRequest("Article", null, sections), PngFile(), CancellationToken.None),
                400, "Cms.Library.InvalidSections", "leak-trace");
            Assert.DoesNotContain("System.Text.Json", problem.Detail ?? string.Empty);
            foreach (string[] messages in Errors(problem).Values)
                foreach (string message in messages)
                {
                    Assert.DoesNotContain("System.Text.Json", message);
                    Assert.DoesNotContain("NullReference", message);
                }
        }
    }

    private static ProblemDetails AssertProblem(
        IActionResult result, int status, string code, string traceId, bool checkTrace = true)
    {
        var response = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, response.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(response.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        if (checkTrace)
            Assert.Equal(traceId, problem.Extensions["traceId"]);
        else
            Assert.True(problem.Extensions.ContainsKey("traceId"));
        return problem;
    }

    private static Dictionary<string, string[]> Errors(ProblemDetails problem)
    {
        Assert.True(problem.Extensions.TryGetValue("errors", out object? errors));
        return Assert.IsType<Dictionary<string, string[]>>(errors);
    }

    private static CreateLibraryContentRequest ArticleRequest(string type, string? categoryId, string? sectionsJson) => new(
        type, categoryId, "عنوان", "Title", "ملخص", "Summary", null, null, false, sectionsJson);

    private static CreateLibraryContentRequest VideoRequest(string? url, int? duration, string? sectionsJson) => new(
        "Video", null, "فيديو", "Video", "ملخص", "Summary", url, duration, false, sectionsJson);

    private static FormFile PngFile()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        var file = new FormFile(new MemoryStream(png), 0, png.Length, "file", "cover.png");
        file.Headers = new HeaderDictionary { ["Content-Type"] = "image/png" };
        return file;
    }

    private static async Task<LibraryContent> CreateArticleAsync(
        CmsDbContext db, Guid? categoryId, string sectionsJson, string cover)
    {
        var sections = System.Text.Json.JsonSerializer.Deserialize<List<LibraryContentSectionRequest>>(
                sectionsJson,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })!
            .Select(item => new LibraryContentSectionInput(
                item.DisplayOrder, item.ArabicTitle, item.EnglishTitle, item.ArabicBullets, item.EnglishBullets))
            .ToList();
        var result = await new CreateLibraryContentCommandHandler(db).Handle(
            new CreateLibraryContentCommand(
                LibraryContentType.Article, categoryId, "عنوان", "Title", "ملخص", "Summary",
                null, null, cover, false, sections),
            CancellationToken.None);
        Assert.True(result.IsSuccess);
        return await ReadAsync(db, result.Value.Id);
    }

    private static async Task<List<LibraryContent>> ReadAllAsync(CmsDbContext db) =>
        await db.LibraryContents
            .AsNoTracking()
            .Include(content => content.Sections)
            .ToListAsync();

    private static async Task<LibraryContent> ReadAsync(CmsDbContext db, Guid id) =>
        await db.LibraryContents
            .AsNoTracking()
            .Include(content => content.Sections)
            .SingleAsync(content => content.Id.Value == id);

    private static CmsDbContext CreateDb()
    {
        var db = new CmsDbContext(new DbContextOptionsBuilder<CmsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static AdminLibraryController CreateController(CmsDbContext db, RecordingStorage storage, string traceId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = traceId;
        return new AdminLibraryController(new DispatchingSender(db), storage)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private sealed class DispatchingSender(CmsDbContext db) : MediatR.ISender
    {
        public object? LastRequest { get; private set; }

        public async Task<TResponse> Send<TResponse>(MediatR.IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            object response = request switch
            {
                CreateLibraryContentCommand command => (object)await new CreateLibraryContentCommandHandler(db).Handle(command, cancellationToken),
                UpdateLibraryContentCommand command => (object)await new UpdateLibraryContentCommandHandler(db).Handle(command, cancellationToken),
                GetLibraryAdminContentQuery query => (object)await new GetLibraryAdminContentQueryHandler(db).Handle(query, cancellationToken),
                _ => throw new NotSupportedException()
            };
            return (TResponse)response;
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : MediatR.IRequest => throw new NotSupportedException();
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(MediatR.IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class RecordingStorage : IFileStorage
    {
        private int _counter;
        public int SaveCount { get; private set; }
        public List<string> SavedKeys { get; } = [];
        public List<string> DeletedKeys { get; } = [];
        public Error? FailSaveWith { get; set; }

        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default)
        {
            if (FailSaveWith is not null)
                return Task.FromResult(Result<StoredFile>.Failure(FailSaveWith));
            SaveCount++;
            string key = $"{folder}/test-{++_counter}.png";
            SavedKeys.Add(key);
            return Task.FromResult<Result<StoredFile>>(new StoredFile(key));
        }

        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            DeletedKeys.Add(key);
            return Task.FromResult(Result.Success());
        }
    }
}
