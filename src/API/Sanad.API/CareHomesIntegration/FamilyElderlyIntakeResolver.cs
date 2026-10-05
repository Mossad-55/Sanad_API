using MediatR;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.FamilyIntake;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Identity.Application.Users;

namespace Sanad.API.CareHomesIntegration;

public sealed class FamilyElderlyIntakeResolver(
    IFamiliesDbContext families,
    ISender sender) : IElderlyIntakeResolver
{
    private static readonly Regex E164PhoneNumber =
        new(@"^\+[1-9][0-9]{1,14}$", RegexOptions.Compiled);

    public async Task<Result<ElderlyIntakeResolution>> ResolveAsync(
        ElderlyIntakeRequest request,
        CancellationToken cancellationToken = default)
    {
        Error? validationError = Validate(request);
        if (validationError is not null)
        {
            return Result<ElderlyIntakeResolution>.Failure(validationError);
        }

        var family = await families.Families
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.FamilyId && item.DeletedOnUtc == null &&
                    (item.OwnerUserId == request.SubmittedByUserId ||
                     item.Members.Any(member =>
                         member.Id == request.SubmittedByUserId &&
                         member.Role == Sanad.Modules.Families.Domain.Families.FamilyRole.Editor)),
                cancellationToken);

        if (family is null)
        {
            return NotFound();
        }

        var elderly = await families.Elderlies
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Id == request.ElderlyId && item.FamilyId == request.FamilyId,
                cancellationToken);

        if (elderly is null)
        {
            return NotFound();
        }

        Result<MyProfileResponse> profileResult = await sender.Send(
            new GetMyProfileQuery(request.SubmittedByUserId),
            cancellationToken);

        if (profileResult.IsFailure)
        {
            return NotFound();
        }

        MyProfileResponse profile = profileResult.Value;
        Result<ElderlyMedicalProfileResponse> medicalResult = await sender.Send(
            new GetElderlyMedicalProfileQuery(
                request.SubmittedByUserId,
                request.ElderlyId),
            cancellationToken);

        if (medicalResult.IsFailure)
        {
            return NotFound();
        }

        ElderlyMedicalProfileResponse medical = medicalResult.Value;
        int age = CalculateAge(elderly.DateOfBirth, request.EgyptLocalReferenceDate);

        return new ElderlyIntakeResolution(
            family.Id,
            elderly.Id,
            elderly.IdentityUserId,
            elderly.ArabicFullName,
            elderly.EnglishFullName,
            age,
            new ElderlyIntakeMedicalProjection(
                medical.BloodType.ToString(),
                medical.HeightCm,
                medical.WeightKg,
                medical.ChronicConditions,
                medical.Allergies
                    .Select(item => new ElderlyIntakeAllergy(
                        item.Category.ToString(), item.Allergen, item.Reaction))
                    .ToList(),
                medical.MedicalHistory
                    .Select(item => new ElderlyIntakeMedicalHistory(
                        item.Year, item.Title, item.Description, item.ProcedureDate))
                    .ToList(),
                medical.UpdatedOnUtc),
            new ElderlyIntakeResponsibleContact(
                string.IsNullOrWhiteSpace(request.ResponsibleContactName)
                    ? profile.EnglishFullName
                    : request.ResponsibleContactName.Trim(),
                string.IsNullOrWhiteSpace(request.ResponsibleContactPhone)
                    ? profile.PhoneNumber
                    : request.ResponsibleContactPhone.Trim(),
                string.IsNullOrWhiteSpace(request.ResponsibleContactRelationship)
                    ? null
                    : request.ResponsibleContactRelationship.Trim()),
            string.IsNullOrWhiteSpace(request.CareNeedsNotes)
                ? null
                : request.CareNeedsNotes.Trim());
    }

    private static Error? Validate(ElderlyIntakeRequest request)
    {
        if (request.SubmittedByUserId == UserId.Empty || request.FamilyId == FamilyId.Empty ||
            request.ElderlyId == ElderlyId.Empty || request.EgyptLocalReferenceDate == default)
        {
            return new Error("CareHomes.FamilyIntake.Invalid", "The intake request is invalid.");
        }

        if (request.CareNeedsNotes?.Trim().Length > Elderly.MaximumHealthNotesLength ||
            request.ResponsibleContactName?.Trim().Length > Elderly.MaximumEmergencyContactNameLength ||
            request.ResponsibleContactRelationship?.Trim().Length > Elderly.MaximumEmergencyContactRelationshipLength ||
            (!string.IsNullOrWhiteSpace(request.ResponsibleContactPhone) &&
             !E164PhoneNumber.IsMatch(request.ResponsibleContactPhone.Trim())))
        {
            return new Error("CareHomes.FamilyIntake.Invalid", "The intake request is invalid.");
        }

        return null;
    }

    private static Result<ElderlyIntakeResolution> NotFound() =>
        Result<ElderlyIntakeResolution>.Failure(
            new Error("CareHomes.FamilyIntake.NotFound", "The selected elderly person was not found."));

    private static int CalculateAge(DateOnly dateOfBirth, DateOnly referenceDate)
    {
        int age = referenceDate.Year - dateOfBirth.Year;
        if (referenceDate < dateOfBirth.AddYears(age))
        {
            age--;
        }

        return age;
    }
}
