using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.UnitTests.Cms;

public sealed class SplashScreenTests
{
    [Fact]
    public void Create_ShouldStartAsDraft()
    {
        SplashScreen screen =
            CreateValid();

        Assert.Equal(
            SplashPublicationStatus.Draft,
            screen.Status);

        Assert.Equal(
            SplashAudience.Family,
            screen.Audience);

        Assert.Equal(
            "#1A73E8",
            screen.BackgroundColor);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldRejectMissingInternalName(
        string internalName)
    {
        Assert.Throws<DomainException>(
            () => CreateValid(
                internalName: internalName));
    }

    [Fact]
    public void Create_ShouldRejectInvalidAudience()
    {
        Assert.Throws<DomainException>(
            () => CreateValid(
                audience: (SplashAudience)99));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1A73E8")]
    [InlineData("#1A73E")]
    [InlineData("#GG0000")]
    public void Create_ShouldRejectInvalidBackgroundColor(
        string backgroundColor)
    {
        Assert.Throws<DomainException>(
            () => CreateValid(
                backgroundColor: backgroundColor));
    }

    [Fact]
    public void Create_ShouldRejectNegativeDisplayOrder()
    {
        Assert.Throws<DomainException>(
            () => CreateValid(
                displayOrder: -1));
    }

    [Fact]
    public void UpdateContent_ShouldStayAtomic_WhenLaterInputIsInvalid()
    {
        SplashScreen screen =
            CreateValid();

        string originalTitle =
            screen.ArabicTitle;

        Assert.Throws<DomainException>(
            () => screen.UpdateContent(
                "عنوان جديد",
                "New Title",
                "وصف جديد",
                "New description",
                "ابدأ",
                "Start",
                "splash/family-1.png",
                "bad-color",
                2));

        Assert.Equal(
            originalTitle,
            screen.ArabicTitle);
    }

    [Fact]
    public void Publish_ShouldMoveDraftToPublished()
    {
        SplashScreen screen =
            CreateValid();

        screen.Publish();

        Assert.Equal(
            SplashPublicationStatus.Published,
            screen.Status);
    }

    [Fact]
    public void Unpublish_ShouldReturnPublishedToDraft()
    {
        SplashScreen screen =
            CreateValid();

        screen.Publish();
        screen.Unpublish();

        Assert.Equal(
            SplashPublicationStatus.Draft,
            screen.Status);
    }

    private static SplashScreen CreateValid(
        string internalName = "family-welcome",
        SplashAudience audience = SplashAudience.Family,
        string backgroundColor = "#1a73e8",
        int displayOrder = 0)
    {
        return SplashScreen.Create(
            internalName,
            audience,
            "مرحبا",
            "Welcome",
            "وصف قصير",
            "Short description",
            "التالي",
            "Next",
            "splash/family-welcome.png",
            backgroundColor,
            displayOrder);
    }
}