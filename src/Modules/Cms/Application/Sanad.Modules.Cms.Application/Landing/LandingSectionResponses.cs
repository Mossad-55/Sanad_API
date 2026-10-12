using Sanad.Modules.Cms.Domain.Landing;

namespace Sanad.Modules.Cms.Application.Landing;

public sealed record LandingSectionPublicItem(
    LandingSectionId Id,
    string Type,
    string ArabicTitle,
    string EnglishTitle,
    string? ArabicBody,
    string? EnglishBody,
    string? ContentJson,
    string? ImagePath,
    int DisplayOrder);

public sealed record LandingStatsResponse(
    int FamiliesCount,
    int MedicalCaregiversCount,
    int CompanionCaregiversCount,
    int ApprovedFacilitiesCount)
{
    public int TotalCaregiversCount => MedicalCaregiversCount + CompanionCaregiversCount;
}
