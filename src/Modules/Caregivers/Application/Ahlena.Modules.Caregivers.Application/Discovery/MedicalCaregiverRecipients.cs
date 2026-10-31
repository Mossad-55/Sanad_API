using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Caregivers.Application.Discovery;

public sealed record MedicalCaregiverRecipientItem(
    CaregiverId CaregiverId,
    UserId UserId,
    string ArabicFullName,
    string EnglishFullName,
    string? AvatarUrl,
    Guid? SpecializationId,
    string? SpecializationArabicName,
    string? SpecializationEnglishName);
