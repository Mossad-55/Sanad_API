using FluentValidation.TestHelper;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Cms.Application.Splash;
using Sanad.Modules.Cms.Domain.Splash;
using Sanad.Modules.Cms.Infrastructure.Persistence;

namespace Sanad.UnitTests.Cms;

public sealed class SplashScreenHandlerTests
{
    [Fact]
    public async Task Create_ShouldPersistDraftSplashScreen()
    {
        await using CmsDbContext dbContext =
            CreateDbContext();

        CreateSplashScreenCommandHandler handler =
            new(dbContext);

        var result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            SplashPublicationStatus.Draft,
            result.Value.Status);
        Assert.Equal(
            "family-welcome",
            result.Value.InternalName);
        Assert.Single(dbContext.SplashScreens);
    }

    [Fact]
    public async Task Create_ShouldRejectDuplicateInternalName()
    {
        await using CmsDbContext dbContext =
            CreateDbContext();

        CreateSplashScreenCommandHandler handler =
            new(dbContext);

        await handler.Handle(
            CreateCommand(),
            CancellationToken.None);

        var result =
            await handler.Handle(
                CreateCommand(),
                CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            SplashErrors.InternalNameAlreadyInUse.Code,
            result.Error.Code);
        Assert.Single(dbContext.SplashScreens);
    }

    [Fact]
    public async Task GetPublished_ShouldReturnEmpty_WhenOnlyDraftExists()
    {
        await using CmsDbContext dbContext =
            CreateDbContext();

        await new CreateSplashScreenCommandHandler(dbContext)
            .Handle(
                CreateCommand(),
                CancellationToken.None);

        var result =
            await new GetPublishedSplashScreensQueryHandler(
                    dbContext)
                .Handle(
                    new GetPublishedSplashScreensQuery(),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task Delete_ShouldRemoveSplashScreen()
    {
        await using CmsDbContext dbContext =
            CreateDbContext();

        var created =
            await new CreateSplashScreenCommandHandler(
                    dbContext)
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        var result =
            await new DeleteSplashScreenCommandHandler(
                    dbContext)
                .Handle(
                    new DeleteSplashScreenCommand(
                        created.Value.Id),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(dbContext.SplashScreens);
    }

    [Fact]
    public async Task Delete_ShouldReturnNotFound_WhenScreenDoesNotExist()
    {
        await using CmsDbContext dbContext =
            CreateDbContext();

        var result =
            await new DeleteSplashScreenCommandHandler(
                    dbContext)
                .Handle(
                    new DeleteSplashScreenCommand(
                        SplashScreenId.New()),
                    CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            SplashErrors.NotFound.Code,
            result.Error.Code);
    }

    [Fact]
    public async Task Publish_ShouldMakeScreenVisibleInPublicQuery()
    {
        await using CmsDbContext dbContext =
            CreateDbContext();

        var created =
            await new CreateSplashScreenCommandHandler(
                    dbContext)
                .Handle(
                    CreateCommand(),
                    CancellationToken.None);

        await new PublishSplashScreenCommandHandler(
                dbContext)
            .Handle(
                new PublishSplashScreenCommand(
                    created.Value.Id),
                CancellationToken.None);

        var result =
            await new GetPublishedSplashScreensQueryHandler(
                    dbContext)
                .Handle(
                    new GetPublishedSplashScreensQuery(),
                    CancellationToken.None);

        Assert.True(result.IsSuccess);
        SplashScreenPublicItem item =
            Assert.Single(result.Value);
        Assert.Equal(
            created.Value.Id,
            item.Id);
    }

    [Fact]
    public async Task Update_ShouldReturnNotFound_WhenScreenDoesNotExist()
    {
        await using CmsDbContext dbContext =
            CreateDbContext();

        var result =
            await new UpdateSplashScreenCommandHandler(
                    dbContext)
                .Handle(
                    new UpdateSplashScreenCommand(
                        SplashScreenId.New(),
                        "عنوان",
                        "Title",
                        "وصف",
                        "Description",
                        "ابدأ",
                        "Start",
                        "splash/family.png",
                        "#1A73E8",
                        0),
                    CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(
            SplashErrors.NotFound.Code,
            result.Error.Code);
    }

    [Fact]
    public void CreateValidator_ShouldRejectEmptyArabicTitle()
    {
        CreateSplashScreenCommandValidator validator =
            new();

        CreateSplashScreenCommand command =
            CreateCommand() with
            {
                ArabicTitle = ""
            };

        TestValidationResult<CreateSplashScreenCommand>
            result =
                validator.TestValidate(
                    command);

        result.ShouldHaveValidationErrorFor(
            value =>
                value.ArabicTitle);
    }

    private static CreateSplashScreenCommand CreateCommand()
    {
        return new CreateSplashScreenCommand(
            "family-welcome",
            "مرحبا",
            "Welcome",
            "وصف قصير",
            "Short description",
            "التالي",
            "Next",
            "splash/family-welcome.png",
            "#1A73E8",
            0);
    }

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<
                CmsDbContext>()
                .UseInMemoryDatabase(
                    Guid.NewGuid().ToString())
                .Options;

        CmsDbContext dbContext =
            new(options);

        dbContext.Database.EnsureCreated();

        return dbContext;
    }
}