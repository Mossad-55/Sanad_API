using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.Families;
using Sanad.Modules.Families.Domain.Elderlies;

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
    public static DependentResponse ToResponse(
        this Elderly elderly) =>
        new(
            elderly.Id,
            elderly.FamilyId,
            elderly.IdentityUserId,
            elderly.ArabicFullName,
            elderly.EnglishFullName,
            elderly.Gender,
            elderly.DateOfBirth,
            !string.IsNullOrWhiteSpace(elderly.ProfileImageKey),
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
    // Mirror of Identity's UserLookupErrors.EmailNotFound code;
    // Families.Application cannot reference Identity.Application.
    private const string ElderlyIdentityNotFoundCode =
        "Identity.Elderly.NotFound";

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
        Domain.Families.Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.OwnerUserId,
                cancellationToken);

        if (family is null)
        {
            return ElderlyErrors.FamilyNotFound;
        }

        // Owner or Editor only.
        if (!FamilyAccess.CanManage(family, request.OwnerUserId))
        {
            return ElderlyErrors.AccessDenied;
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
        Result<ElderlyIdentityAccount> lookup =
            await _identityGateway.GetElderlyByPhoneAsync(
                phone.Value,
                cancellationToken);

        UserId identityUserId;
        bool identityCreated = false;

        if (lookup.IsSuccess)
        {
            if (!lookup.Value.IsElderly)
            {
                // Phone belongs to a family/caregiver/admin account.
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

            // Existing elderly Identity user (e.g. re-add after removal) —
            // link to it, never re-create.
            identityUserId = lookup.Value.UserId;
        }
        else
        {
            if (lookup.Error.Code != ElderlyIdentityNotFoundCode)
            {
                return Result<DependentResponse>.Failure(
                    lookup.Error);
            }

            // No Identity user for this phone -> create the elderly login.
            Result<ElderlyIdentityAccount> created =
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
            identityCreated = true;
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
                request.CurrentDate,
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
            if (identityCreated)
            {
                await _identityGateway.DeleteElderlyAsync(
                    identityUserId,
                    cancellationToken);
            }

            return ElderlyErrors.InvalidProfile;
        }

        _dbContext.Elderlies.Add(elderly);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Cross-module compensation: Families write failed after a
            // fresh Identity user was created.
            if (identityCreated)
            {
                await _identityGateway.DeleteElderlyAsync(
                    identityUserId,
                    cancellationToken);
            }

            throw;
        }

        return elderly.ToResponse();
    }
}

// ------------------------------- List ---------------------------------

public sealed record ListDependentsQuery(
    UserId UserId)
    : IQuery<IReadOnlyList<DependentResponse>>;

public sealed class ListDependentsQueryHandler
    : IQueryHandler<
        ListDependentsQuery,
        IReadOnlyList<DependentResponse>>
{
    private readonly IFamiliesDbContext _dbContext;

    public ListDependentsQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<DependentResponse>>> Handle(
        ListDependentsQuery request,
        CancellationToken cancellationToken)
    {
        Domain.Families.Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.UserId,
                cancellationToken);

        if (family is null)
        {
            return Result<IReadOnlyList<DependentResponse>>.Failure(
                ElderlyErrors.FamilyNotFound);
        }

        // Any family member can view dependents.
        List<Elderly> dependents =
            await _dbContext.Elderlies
                .AsNoTracking()
                .Where(e => e.FamilyId == family.Id)
                .OrderBy(e => e.CreatedOnUtc)
                .ToListAsync(cancellationToken);

        IReadOnlyList<DependentResponse> items =
            dependents
                .Select(e => e.ToResponse())
                .ToList();

        return Result<IReadOnlyList<DependentResponse>>.Success(items);
    }
}

// -------------------------------- Get ---------------------------------

public sealed record GetDependentQuery(
    UserId UserId,
    ElderlyId DependentId)
    : IQuery<DependentResponse>;

public sealed class GetDependentQueryValidator
    : AbstractValidator<GetDependentQuery>
{
    public GetDependentQueryValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
    }
}

public sealed class GetDependentQueryHandler
    : IQueryHandler<GetDependentQuery, DependentResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public GetDependentQueryHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<DependentResponse>> Handle(
        GetDependentQuery request,
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

        return elderly.ToResponse();
    }
}

// ------------------------------- Update -------------------------------

public sealed record UpdateDependentCommand(
    UserId UserId,
    ElderlyId DependentId,
    string ArabicFullName,
    string EnglishFullName,
    Gender Gender,
    DateOnly DateOfBirth,
    string? DetailedAddress,
    string? HealthNotes,
    DateOnly CurrentDate)
    : ICommand<DependentResponse>;

public sealed class UpdateDependentCommandValidator
    : AbstractValidator<UpdateDependentCommand>
{
    public UpdateDependentCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
        RuleFor(c => c.ArabicFullName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.EnglishFullName).NotEmpty().MaximumLength(200);
        RuleFor(c => c.Gender).IsInEnum();
        RuleFor(c => c.DateOfBirth)
            .LessThanOrEqualTo(c => c.CurrentDate)
            .WithMessage("Date of birth cannot be in the future.");
        RuleFor(c => c.DetailedAddress)
            .MaximumLength(Elderly.MaximumDetailedAddressLength)
            .When(c => c.DetailedAddress is not null);
        RuleFor(c => c.HealthNotes)
            .MaximumLength(Elderly.MaximumHealthNotesLength)
            .When(c => c.HealthNotes is not null);
    }
}

public sealed class UpdateDependentCommandHandler
    : ICommandHandler<UpdateDependentCommand, DependentResponse>
{
    private readonly IFamiliesDbContext _dbContext;

    public UpdateDependentCommandHandler(IFamiliesDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<DependentResponse>> Handle(
        UpdateDependentCommand request,
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

        FullName arabicName;
        FullName englishName;
        try
        {
            arabicName = FullName.Create(request.ArabicFullName);
            englishName = FullName.Create(request.EnglishFullName);
        }
        catch (DomainException)
        {
            return ElderlyErrors.InvalidProfile;
        }

        try
        {
            // Photo is managed separately (SetDependentPhotoCommand) and
            // is intentionally untouched by a profile update.
            elderly.UpdateProfile(
                arabicName,
                englishName,
                request.Gender,
                request.DateOfBirth,
                request.CurrentDate,
                string.IsNullOrWhiteSpace(request.DetailedAddress)
                    ? null
                    : request.DetailedAddress.Trim(),
                string.IsNullOrWhiteSpace(request.HealthNotes)
                    ? null
                    : request.HealthNotes.Trim());
        }
        catch (DomainException)
        {
            return ElderlyErrors.InvalidProfile;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return elderly.ToResponse();
    }
}

// ------------------------------- Remove -------------------------------

public sealed record RemoveDependentCommand(
    UserId UserId,
    ElderlyId DependentId)
    : ICommand;

public sealed class RemoveDependentCommandValidator
    : AbstractValidator<RemoveDependentCommand>
{
    public RemoveDependentCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.DependentId).NotEqual(ElderlyId.Empty);
    }
}

public sealed class RemoveDependentCommandHandler
    : ICommandHandler<RemoveDependentCommand>
{
    private readonly IFamiliesDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public RemoveDependentCommandHandler(
        IFamiliesDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result> Handle(
        RemoveDependentCommand request,
        CancellationToken cancellationToken)
    {
        Domain.Families.Family? family =
            await FamilyAccess.ResolveFamilyAsync(
                _dbContext,
                request.UserId,
                cancellationToken);

        if (family is null)
        {
            return Result.Failure(ElderlyErrors.FamilyNotFound);
        }

        if (!FamilyAccess.CanManage(family, request.UserId))
        {
            return Result.Failure(ElderlyErrors.AccessDenied);
        }

        Elderly? elderly =
            await _dbContext.Elderlies
                .SingleOrDefaultAsync(
                    e => e.Id == request.DependentId &&
                         e.FamilyId == family.Id,
                    cancellationToken);

        if (elderly is null)
        {
            return Result.Failure(ElderlyErrors.NotFound);
        }

        string? photoKey = elderly.ProfileImageKey;

        // Hard delete the Families row. The Elderly Identity user is
        // intentionally LEFT in place: it can still log in by phone OTP
        // and can be re-linked by a family later.
        _dbContext.Elderlies.Remove(elderly);

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(photoKey))
        {
            // Best-effort orphan cleanup; never fails the operation.
            await _fileStorage.DeleteAsync(
                photoKey,
                cancellationToken);
        }

        return Result.Success();
    }
}