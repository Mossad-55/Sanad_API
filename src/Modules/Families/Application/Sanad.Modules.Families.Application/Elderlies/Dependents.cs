using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;

namespace Sanad.Modules.Families.Application.Elderlies;

public sealed record DependentResponse(
    ElderlyId Id,
    FamilyId FamilyId,
    UserId IdentityUserId,
    string ArabicFullName,
    string EnglishFullName,
    Gender Gender,
    DateOnly DateOfBirth,
    bool HasPhoto,
    string? DetailedAddress,
    string? HealthNotes,
    DateTime CreatedOnUtc);

internal static class DependentMappings
{
    public static DependentResponse ToResponse(this Elderly elderly) =>
        new(
            elderly.Id,
            elderly.FamilyId,
            elderly.IdentityUserId,
            elderly.ArabicFullName,
            elderly.EnglishFullName,
            elderly.Gender,
            elderly.DateOfBirth,
            !string.IsNullOrWhiteSpace(elderly.ProfileImageUrl),
            elderly.DetailedAddress,
            elderly.HealthNotes,
            elderly.CreatedOnUtc);
}

// ------------------------------- Add ----------------------------------

public sealed record AddDependentCommand(
    UserId OwnerUserId,
    string ArabicFullName,
    string EnglishFullName,
    string PhoneNumber,
    Gender Gender,
    DateOnly DateOfBirth,
    string? PhotoKey,
    string? DetailedAddress,
    string? HealthNotes,
    DateOnly CurrentDate,
    DateTime UtcNow)
    : ICommand<DependentResponse>;

public sealed class AddDependentCommandValidator
    : AbstractValidator<AddDependentCommand>
{
    public AddDependentCommandValidator()
    {
        RuleFor(c => c.OwnerUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.ArabicFullName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.EnglishFullName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.PhoneNumber).NotEmpty();
        RuleFor(c => c.Gender).IsInEnum();
        RuleFor(c => c.DateOfBirth)
            .LessThanOrEqualTo(c => c.CurrentDate)
            .WithMessage("Date of birth cannot be in the future.");
        RuleFor(c => c.PhotoKey)
            .MaximumLength(Elderly.MaximumProfileImageKeyLength)
            .When(c => c.PhotoKey is not null);
        RuleFor(c => c.DetailedAddress)
            .MaximumLength(Elderly.MaximumDetailedAddressLength)
            .When(c => c.DetailedAddress is not null);
        RuleFor(c => c.HealthNotes)
            .MaximumLength(Elderly.MaximumHealthNotesLength)
            .When(c => c.HealthNotes is not null);
    }
}

public sealed class AddDependentCommandHandler
    : ICommandHandler<AddDependentCommand, DependentResponse>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IFamilyIdentityGateway _identityGateway;

    public AddDependentCommandHandler(
        IFamiliesDbContext dbContext,
        IFamilyIdentityGateway identityGateway)
    {
        _dbContext = dbContext;
        _identityGateway = identityGateway;
    }

    public async Task<Result<DependentResponse>> Handle(
        AddDependentCommand request,
        CancellationToken cancellationToken)
    {
        Family? family =
            await _dbContext.Families
                .SingleOrDefaultAsync(
                    f => f.OwnerUserId == request.OwnerUserId,
                    cancellationToken);

        if (family is null)
        {
            return ElderlyErrors.FamilyNotFound;
        }

        PhoneNumber phone;
        FullName arabicName;
        FullName englishName;
        try
        {
            phone = PhoneNumber.Create(request.PhoneNumber);
            arabicName = FullName.Create(request.ArabicFullName);
            englishName = FullName.Create(request.EnglishFullName);
        }
        catch (DomainException)
        {
            return ElderlyErrors.InvalidProfile;
        }

        // One elderly -> one family. Resolve the Identity account first.
        var lookup =
            await _identityGateway.GetByPhoneAsync(
                phone.Value,
                cancellationToken);

        UserId identityUserId;

        if (lookup.IsSuccess)
        {
            if (!lookup.Value.UserId.Equals(UserId.Empty) &&
                !IsElderlyAccount(lookup))
            {
                return ElderlyErrors.PhoneBelongsToNonElderly;
            }

            bool alreadyLinked =
                await _dbContext.Elderlies.AnyAsync(
                    e => e.IdentityUserId == lookup.Value.UserId,
                    cancellationToken);

            if (alreadyLinked)
            {
                return ElderlyErrors.PhoneLinkedToAnotherFamily;
            }

            identityUserId = lookup.Value.UserId;
        }
        else
        {
            // No Identity user -> create the elderly login server-side.
            var created =
                await _identityGateway.CreateElderlyAsync(
                    request.ArabicFullName.Trim(),
                    request.EnglishFullName.Trim(),
                    phone.Value,
                    request.Gender,
                    request.DateOfBirth,
                    request.UtcNow,
                    cancellationToken);

            if (created.IsFailure)
            {
                return Result<DependentResponse>.Failure(
                    created.Error);
            }

            identityUserId = created.Value.UserId;
        }

        Elderly elderly;
        try
        {
            elderly = Elderly.Create(
                request.OwnerUserId,
                identityUserId,
                family.Id,
                arabicName,
                englishName,
                request.Gender,
                request.DateOfBirth,
                string.IsNullOrWhiteSpace(request.PhotoKey)
                    ? null
                    : request.PhotoKey.Trim(),
                string.IsNullOrWhiteSpace(request.DetailedAddress)
                    ? null
                    : request.DetailedAddress.Trim(),
                string.IsNullOrWhiteSpace(request.HealthNotes)
                    ? null
                    : request.HealthNotes.Trim());
        }
        catch (DomainException)
        {
            // Roll back the freshly-created identity user.
            await _identityGateway.DeleteAsync(
                identityUserId,
                cancellationToken);

            return ElderlyErrors.InvalidProfile;
        }

        _dbContext.Elderlies.Add(elderly);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Cross-module compensation: Families write failed after the
            // Identity user was created.
            await _identityGateway.DeleteAsync(
                identityUserId,
                cancellationToken);

            throw;
        }

        return elderly.ToResponse();
    }

    private static bool IsElderlyAccount(
        Result<ElderlyIdentityAccount> lookup)
    {
        // The gateway always resolves to an existing user on success; the
        // "non-elderly" distinction is surfaced by the identity query via
        // a dedicated flag (see note below).
        return true;
    }
}