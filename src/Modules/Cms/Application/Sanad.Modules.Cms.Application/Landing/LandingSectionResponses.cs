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
    int DisplayOrder,
    IReadOnlyList<TestimonialItem>? Testimonials = null,
    IReadOnlyList<LandingFaqItem>? Faqs = null);

public sealed record TestimonialItem(
    string Kind,
    Guid RefId,
    int Stars,
    string? Text,
    string ArabicName,
    string EnglishName);

public sealed record LandingFaqItem(
    Guid RefId,
    string ArabicQuestion,
    string EnglishQuestion,
    string ArabicAnswer,
    string EnglishAnswer);

public sealed record LandingStatsResponse(
    int FamiliesCount,
    int MedicalCaregiversCount,
    int CompanionCaregiversCount,
    int ApprovedFacilitiesCount)
{
    public int TotalCaregiversCount => MedicalCaregiversCount + CompanionCaregiversCount;
}
