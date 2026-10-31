using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Families.Application.Abstractions.Caregivers;

public sealed record MedicalCaregiverRecipient(
    Guid CaregiverId,
    Guid UserId,
    string ArabicFullName,
    string EnglishFullName,
    string? AvatarUrl,
    Guid? SpecializationId,
    string? SpecializationArabicName,
    string? SpecializationEnglishName);

public sealed record MedicalCaregiverRecipientPage(
    IReadOnlyList<MedicalCaregiverRecipient> Items,
    int TotalCount);
