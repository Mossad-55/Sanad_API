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

    [Fact]
    public void RemoveLanguage_ShouldRemoveFinalLanguageDuringOnboarding()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Language language =
            CreateLanguage(
                "ar",
                "العربية",
                "Arabic");

        caregiver.SelectLanguage(language);

        caregiver.RemoveLanguage(language.Id);

        Assert.Empty(
            caregiver.LanguageSelections);

        Assert.Equal(
            CaregiverStatus.Onboarding,
            caregiver.Status);

        Assert.True(
            caregiver.UpdatedOnUtc >=
            caregiver.CreatedOnUtc);
    }

    [Fact]
    public void RemoveLanguage_ShouldAllowActiveCaregiverToKeepOneLanguage()
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
        caregiver.TransitionToActive();

        caregiver.RemoveLanguage(arabic.Id);

        var remainingSelection =
            Assert.Single(
                caregiver.LanguageSelections);

        Assert.Equal(
            english.Id,
            remainingSelection.Id);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);
    }

    [Fact]
    public void RemoveLanguage_ShouldRejectFinalLanguageForActiveCaregiver()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        Language language =
            CreateLanguage(
                "ar",
                "العربية",
                "Arabic");

        caregiver.SelectLanguage(language);
        caregiver.TransitionToActive();

        DateTime updatedOnUtcBeforeRemoval =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveLanguage(
                language.Id));

        var selection =
            Assert.Single(
                caregiver.LanguageSelections);

        Assert.Equal(
            language.Id,
            selection.Id);

        Assert.Equal(
            CaregiverStatus.Active,
            caregiver.Status);

        Assert.Equal(
            updatedOnUtcBeforeRemoval,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveLanguage_ShouldRejectUnselectedLanguage()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Companion);

        Language selectedLanguage =
            CreateLanguage(
                "ar",
                "العربية",
                "Arabic");

        Language unselectedLanguage =
            CreateLanguage(
                "en",
                "الإنجليزية",
                "English");

        caregiver.SelectLanguage(
            selectedLanguage);

        DateTime updatedOnUtcBeforeRemoval =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveLanguage(
                unselectedLanguage.Id));

        var selection =
            Assert.Single(
                caregiver.LanguageSelections);

        Assert.Equal(
            selectedLanguage.Id,
            selection.Id);

        Assert.Equal(
            updatedOnUtcBeforeRemoval,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveLanguage_ShouldRejectEmptyLanguageId()
    {
        Caregiver caregiver =
            CreateCaregiver(
                CaregiverType.Medical);

        DateTime originalUpdatedOnUtc =
            caregiver.UpdatedOnUtc;

        Assert.Throws<DomainException>(
            () => caregiver.RemoveLanguage(
                LanguageId.Empty));

        Assert.Empty(
            caregiver.LanguageSelections);

        Assert.Equal(
            originalUpdatedOnUtc,
            caregiver.UpdatedOnUtc);
    }

    [Fact]
    public void RemoveLanguage_ShouldAllowSuspendedCaregiverToRemoveFinalLanguage()
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
        caregiver.TransitionToActive();
        caregiver.TransitionToSuspended();

        caregiver.RemoveLanguage(language.Id);

        Assert.Empty(
            caregiver.LanguageSelections);

        Assert.Equal(
            CaregiverStatus.Suspended,
            caregiver.Status);
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