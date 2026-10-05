using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.CareHomes.Application.FamilyIntake;

public interface IElderlyIntakeResolver
{
    Task<Result<ElderlyIntakeResolution>> ResolveAsync(
        ElderlyIntakeRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ElderlyIntakeRequest(
    UserId SubmittedByUserId,
    FamilyId FamilyId,
    ElderlyId ElderlyId,
    DateOnly EgyptLocalReferenceDate,
    string? CareNeedsNotes = null,
    string? ResponsibleContactName = null,
    string? ResponsibleContactPhone = null,
    string? ResponsibleContactRelationship = null);

public sealed record ElderlyIntakeResolution(
    FamilyId FamilyId,
    ElderlyId ElderlyId,
    UserId ElderlyIdentityUserId,
    string ArabicFullName,
    string EnglishFullName,
    int Age,
    ElderlyIntakeMedicalProjection MedicalProfile,
    ElderlyIntakeResponsibleContact ResponsibleContact,
    string? CareNeedsNotes);

public sealed record ElderlyIntakeResponsibleContact(
    string Name,
    string? PhoneNumber,
    string? Relationship);

public sealed record ElderlyIntakeMedicalProjection(
    string BloodType,
    int? HeightCm,
    decimal? WeightKg,
    IReadOnlyList<string> ChronicConditions,
    IReadOnlyList<ElderlyIntakeAllergy> Allergies,
    IReadOnlyList<ElderlyIntakeMedicalHistory> MedicalHistory,
    DateTime? UpdatedOnUtc);

public sealed record ElderlyIntakeAllergy(
    string Category,
    string Allergen,
    string? Reaction);

public sealed record ElderlyIntakeMedicalHistory(
    int? Year,
    string Title,
    string? Description,
    DateOnly? ProcedureDate);
