using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Domain.Caregivers.Lookups;

namespace Sanad.UnitTests.Caregivers;

public sealed class CaregiverLanguageSelectionTests
{
    [Theory]
    [InlineData(CaregiverType.Medical)]
    [InlineData(CaregiverType.Companion)]
    public void SelectLanguage_ShouldAddActiveLanguage(
        CaregiverType caregiverType)
    {
        Caregiver caregiver =
            CreateCaregiver(caregiverType);

        Language language =
            CreateLanguage(
                "ar",
                "العربية",
                "Arabic");

        caregiver.SelectLanguage(language);

        var selection =
            Assert.Single(
                caregiver.LanguageSelections);

        Assert.Equal(
            language.Id,
            selection.Id);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Fact]
    public void SelectLanguage_ShouldAllowMultipleDifferentLanguages()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Language arabic =
            CreateLanguage(
                "ar",
                "العربية",
                "Arabic");

        Language english =
            CreateLanguage(
                "en",
                "الإنجليزية",
                "English");

        caregiver.SelectLanguage(arabic);
        caregiver.SelectLanguage(english);

        Assert.Equal(
            2,
            caregiver.LanguageSelections.Count);

        Assert.Contains(
            caregiver.LanguageSelections,
            selection =>
                selection.Id == arabic.Id);

        Assert.Contains(
            caregiver.LanguageSelections,
            selection =>
                selection.Id == english.Id);
    }

    [Fact]
    public void SelectLanguage_ShouldRejectInactiveLanguage()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Language language =
            CreateLanguage(
                "ar",
                "العربية",
                "Arabic");

        language.Deactivate();

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SelectLanguage(
                language));

        Assert.Empty(
            caregiver.LanguageSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SelectLanguage_ShouldRejectDuplicateLanguage()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Language language =
            CreateLanguage(
                "ar",
                "العربية",
                "Arabic");

        caregiver.SelectLanguage(language);

        DateTime updatedOnUtcAfterSelection =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.SelectLanguage(
                language));

        var selection =
            Assert.Single(
                caregiver.LanguageSelections);

        Assert.Equal(
            language.Id,
            selection.Id);

        Assert.Equal(
            updatedOnUtcAfterSelection,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void SelectLanguage_ShouldRejectNullLanguage()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<ArgumentNullException>(
            () => caregiver.SelectLanguage(
                null!));

        Assert.Empty(
            caregiver.LanguageSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    private static Caregiver CreateCaregiver(
        CaregiverType caregiverType)
    {
        return Caregiver.Create(
            UserId.New(),
            caregiverType);
    }

    private static Language CreateLanguage(
        string code,
        string arabicName,
        string englishName)
    {
        return Language.Create(
            code,
            arabicName,
            englishName);
    }
}