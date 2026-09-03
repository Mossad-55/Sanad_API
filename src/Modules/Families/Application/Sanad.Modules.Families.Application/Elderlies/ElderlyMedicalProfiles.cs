using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.Medical;

namespace Sanad.Modules.Families.Application.Elderlies;

// --------------------------------- DTOs ---------------------------------

public sealed record AllergyDto(
    AllergyCategory Category,
    string Allergen,
    string? Reaction);

public sealed record MedicalHistoryDto(
    int? Year,
    string Title,
    string? Description);

public sealed record ElderlyMedicalProfileResponse(
    ElderlyId DependentId,
    BloodType BloodType,
    int? HeightCm,
    decimal? WeightKg,
    IReadOnlyList<string> ChronicConditions,
    IReadOnlyList<AllergyDto> Allergies,
    IReadOnlyList<MedicalHistoryDto> MedicalHistory,
    DateTime? UpdatedOnUtc);

internal static class ElderlyMedicalProfileMappings
{
    public static ElderlyMedicalProfileResponse ToResponse(
        ElderlyId dependentId,
        ElderlyMedicalProfile? profile)
    {
        if (profile is null)
        {
            return new ElderlyMedicalProfileResponse(
                dependentId,
                BloodType.Unknown,
                null,
                null,
                [],
                [],
                [],
                null);
        }

        return new ElderlyMedicalProfileResponse(
            dependentId,
            profile.BloodType,
            profile.HeightCm,
            profile.WeightKg,
            profile.ChronicConditions,
            profile.Allergies
                .Select(a => new AllergyDto(a.Category, a.Allergen, a.Reaction))
                .ToList(),
            profile.MedicalHistory
                .Select(h => new MedicalHistoryDto(h.Year, h.Title, h.Description))
                .ToList(),
            profile.UpdatedOnUtc);
    }
}

// --------------------------------- Get ----------------------------------

public sealed record GetElderlyMedicalProfileQuery(
    UserId UserId,
    ElderlyId DependentId)
    : IQuery<ElderlyMedicalProfileResponse>;

public sealed class GetElderlyMedicalProfileQueryValidator
    : AbstractValidator<GetElderlyMedicalProfileQuery>
{
    public GetElderlyMedicalProfileQueryValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
    }
}

public sealed class GetElderlyMedicalProfileQueryHandler
    : IQueryHandler<GetElderlyMedicalProfileQuery, ElderlyMedicalProfileResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetElderlyMedicalProfileQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ElderlyMedicalProfileResponse>> Handle(
        GetElderlyMedicalProfileQuery request,
        CancellationToken cancellationToken)
    {
        Domain.Families.Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.UserId,
                cancellationToken);

        if (family is null)
        {
            return ElderlyErrors.FamilyNotFound;
        }

        Elderly? elderly =
            await _dbContext.Elderlies
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    e => e.Id == request.DependentId &&
                         e.FamilyId == family.Id,
                    cancellationToken);

        if (elderly is null)
        {
            return ElderlyErrors.NotFound;
        }

        return ElderlyMedicalProfileMappings.ToResponse(
            elderly.Id,
            elderly.MedicalProfile);
    }
}

// -------------------------------- Update --------------------------------

public sealed record UpdateElderlyMedicalProfileCommand(
    UserId UserId,
    ElderlyId DependentId,
    BloodType BloodType,
    int? HeightCm,
    decimal? WeightKg,
    IReadOnlyList<string>? ChronicConditions,
    IReadOnlyList<AllergyDto>? Allergies,
    IReadOnlyList<MedicalHistoryDto>? MedicalHistory)
    : ICommand<ElderlyMedicalProfileResponse>;

public sealed class UpdateElderlyMedicalProfileCommandValidator
    : AbstractValidator<UpdateElderlyMedicalProfileCommand>
{
    public UpdateElderlyMedicalProfileCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.BloodType).IsInEnum();

        RuleFor(c => c.HeightCm)
            .InclusiveBetween(
                ElderlyMedicalProfile.MinimumHeightCm,
                ElderlyMedicalProfile.MaximumHeightCm)
            .When(c => c.HeightCm.HasValue);

        RuleFor(c => c.WeightKg)
            .InclusiveBetween(
                ElderlyMedicalProfile.MinimumWeightKg,
                ElderlyMedicalProfile.MaximumWeightKg)
            .When(c => c.WeightKg.HasValue);

        RuleForEach(c => c.Allergies).ChildRules(allergy =>
        {
            allergy.RuleFor(a => a.Category).IsInEnum();
            allergy.RuleFor(a => a.Allergen)
                .NotEmpty()
                .MaximumLength(AllergyEntry.MaximumAllergenLength);
            allergy.RuleFor(a => a.Reaction)
                .MaximumLength(AllergyEntry.MaximumReactionLength)
                .When(a => a.Reaction is not null);
        });

        RuleForEach(c => c.MedicalHistory).ChildRules(history =>
        {
            history.RuleFor(h => h.Title)
                .NotEmpty()
                .MaximumLength(MedicalHistoryEntry.MaximumTitleLength);
            history.RuleFor(h => h.Year)
                .InclusiveBetween(1900, 2100)
                .When(h => h.Year.HasValue);
            history.RuleFor(h => h.Description)
                .MaximumLength(MedicalHistoryEntry.MaximumDescriptionLength)
                .When(h => h.Description is not null);
        });
    }
}

public sealed class UpdateElderlyMedicalProfileCommandHandler
    : ICommandHandler<UpdateElderlyMedicalProfileCommand, ElderlyMedicalProfileResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public UpdateElderlyMedicalProfileCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<ElderlyMedicalProfileResponse>> Handle(
        UpdateElderlyMedicalProfileCommand request,
        CancellationToken cancellationToken)
    {
        Domain.Families.Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.UserId,
                cancellationToken);

        if (family is null)
        {
            return ElderlyErrors.FamilyNotFound;
        }

        if (!FamilyAccess.CanManage(family, request.UserId))
        {
            return ElderlyErrors.AccessDenied;
        }

        Elderly? elderly =
            await _dbContext.Elderlies
                .SingleOrDefaultAsync(
                    e => e.Id == request.DependentId &&
                         e.FamilyId == family.Id,
                    cancellationToken);

        if (elderly is null)
        {
            return ElderlyErrors.NotFound;
        }

        try
        {
            var allergies = (request.Allergies ?? [])
                .Select(a => AllergyEntry.Create(a.Category, a.Allergen, a.Reaction));

            var history = (request.MedicalHistory ?? [])
                .Select(h => MedicalHistoryEntry.Create(h.Year, h.Title, h.Description));

            if (elderly.MedicalProfile is null)
            {
                var profile = ElderlyMedicalProfile.Create(
                    request.BloodType,
                    request.HeightCm,
                    request.WeightKg,
                    request.ChronicConditions,
                    allergies,
                    history);

                elderly.UpdateMedicalProfile(profile);
            }
            else
            {
                elderly.MedicalProfile.Update(
                    request.BloodType,
                    request.HeightCm,
                    request.WeightKg,
                    request.ChronicConditions,
                    allergies,
                    history);
            }
        }
        catch (DomainException)
        {
            return ElderlyErrors.InvalidProfile;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return ElderlyMedicalProfileMappings.ToResponse(
            elderly.Id,
            elderly.MedicalProfile);
    }
}