using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers.Lookups;

public sealed class LanguageTests
{
    [Fact]
    public void Create_ShouldCreateActiveLanguage()
    {
        Language language = Language.Create(
            "ar",
            "العربية",
            "Arabic");

        Assert.NotEqual(
            LanguageId.Empty,
            language.Id);

        Assert.Equal("ar", language.Code);
        Assert.Equal("العربية", language.ArabicName);
        Assert.Equal("Arabic", language.EnglishName);
        Assert.True(language.IsActive);

        Assert.Equal(
            language.CreatedOnUtc,
            language.UpdatedOnUtc);
    }

    [Theory]
    [InlineData("ar", "ar")]
    [InlineData(" EN ", "en")]
    [InlineData("fra", "fra")]
    [InlineData(" ENG ", "eng")]
    public void Create_ShouldNormalizeValidLanguageCode(
        string code,
        string expectedCode)
    {
        Language language = Language.Create(
            code,
            "لغة",
            "Language");

        Assert.Equal(
            expectedCode,
            language.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a")]
    [InlineData("abcd")]
    [InlineData("ar-EG")]
    [InlineData("a1")]
    [InlineData("عربي")]
    public void Create_ShouldRejectInvalidLanguageCode(
        string? code)
    {
        Assert.Throws<DomainException>(
            () => Language.Create(
                code!,
                "العربية",
                "Arabic"));
    }

    [Theory]
    [InlineData(null, "Arabic")]
    [InlineData("", "Arabic")]
    [InlineData("   ", "Arabic")]
    [InlineData("العربية", null)]
    [InlineData("العربية", "")]
    [InlineData("العربية", "   ")]
    public void Create_ShouldRejectMissingLanguageName(
        string? arabicName,
        string? englishName)
    {
        Assert.Throws<DomainException>(
            () => Language.Create(
                "ar",
                arabicName!,
                englishName!));
    }

    [Fact]
    public void Create_ShouldRejectArabicNameThatIsTooLong()
    {
        string longArabicName = new(
            'أ',
            Language.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Language.Create(
                "ar",
                longArabicName,
                "Arabic"));
    }

    [Fact]
    public void Create_ShouldRejectEnglishNameThatIsTooLong()
    {
        string longEnglishName = new(
            'A',
            Language.MaximumNameLength + 1);

        Assert.Throws<DomainException>(
            () => Language.Create(
                "en",
                "الإنجليزية",
                longEnglishName));
    }

    [Fact]
    public void Create_ShouldTrimLanguageNames()
    {
        Language language = Language.Create(
            "ar",
            "  العربية  ",
            "  Arabic  ");

        Assert.Equal(
            "العربية",
            language.ArabicName);

        Assert.Equal(
            "Arabic",
            language.EnglishName);
    }

    [Fact]
    public void UpdateNames_ShouldUpdateAndTrimNames()
    {
        Language language = Language.Create(
            "en",
            "الإنجليزية",
            "English");

        LanguageId originalId = language.Id;
        string originalCode = language.Code;

        language.UpdateNames(
            "  اللغة الإنجليزية  ",
            "  English Language  ");

        Assert.Equal(originalId, language.Id);
        Assert.Equal(originalCode, language.Code);

        Assert.Equal(
            "اللغة الإنجليزية",
            language.ArabicName);

        Assert.Equal(
            "English Language",
            language.EnglishName);

        Assert.True(language.IsActive);

        Assert.True(
            language.UpdatedOnUtc >=
            language.CreatedOnUtc);
    }

    [Fact]
    public void UpdateNames_ShouldRejectInvalidName()
    {
        Language language = Language.Create(
            "en",
            "الإنجليزية",
            "English");

        Assert.Throws<DomainException>(
            () => language.UpdateNames(
                "",
                "English Language"));

        Assert.Equal(
            "الإنجليزية",
            language.ArabicName);

        Assert.Equal(
            "English",
            language.EnglishName);
    }

    [Fact]
    public void UpdateNames_ShouldNotPartiallyUpdate_WhenEnglishNameIsInvalid()
    {
        Language language = Language.Create(
            "en",
            "الإنجليزية",
            "English");

        DateTime originalUpdatedOnUtc =
            language.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => language.UpdateNames(
                "اللغة الإنجليزية",
                ""));

        Assert.Equal(
            "الإنجليزية",
            language.ArabicName);

        Assert.Equal(
            "English",
            language.EnglishName);

        Assert.Equal(
            "en",
            language.Code);

        Assert.True(language.IsActive);

        Assert.Equal(
            originalUpdatedOnUtc,
            language.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldMakeLanguageInactive()
    {
        Language language = Language.Create(
            "ar",
            "العربية",
            "Arabic");

        language.Deactivate();

        Assert.False(language.IsActive);

        Assert.True(
            language.UpdatedOnUtc >=
            language.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldMakeLanguageActive()
    {
        Language language = Language.Create(
            "ar",
            "العربية",
            "Arabic");

        language.Deactivate();
        language.Activate();

        Assert.True(language.IsActive);

        Assert.True(
            language.UpdatedOnUtc >=
            language.CreatedOnUtc);
    }

    [Fact]
    public void Activate_ShouldDoNothing_WhenAlreadyActive()
    {
        Language language = Language.Create(
            "ar",
            "العربية",
            "Arabic");

        DateTime updatedOnUtc =
            language.UpdatedOnUtc;

        language.Activate();

        Assert.True(language.IsActive);

        Assert.Equal(
            updatedOnUtc,
            language.UpdatedOnUtc);
    }

    [Fact]
    public void Deactivate_ShouldDoNothing_WhenAlreadyInactive()
    {
        Language language = Language.Create(
            "ar",
            "العربية",
            "Arabic");

        language.Deactivate();

        DateTime updatedOnUtc =
            language.UpdatedOnUtc;

        language.Deactivate();

        Assert.False(language.IsActive);

        Assert.Equal(
            updatedOnUtc,
            language.UpdatedOnUtc);
    }
}