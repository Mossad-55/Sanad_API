using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Caregivers.Application.Discovery;

public sealed record MedicalCaregiverRecipientItem(
    CaregiverId CaregiverId,
    UserId UserId,
    string ArabicFullName,
    string EnglishFullName,
    string? AvatarUrl,
    Guid? SpecializationId,
    string? SpecializationArabicName,
    string? SpecializationEnglishName);
