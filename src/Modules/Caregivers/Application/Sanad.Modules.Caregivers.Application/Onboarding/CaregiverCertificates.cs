using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Caregivers.Application.Abstractions.Data;
using Sanad.Modules.Caregivers.Domain.Caregivers;

namespace Sanad.Modules.Caregivers.Application.Onboarding;

public static class CertificateStorage
{
    public const string Folder =
        "caregiver-certificates";
}

// ---------------------- Self-service: add ----------------------------

public sealed record AddCertificateCommand(
    UserId UserId,
    CaregiverCertificateType Type,
    string FileKey,
    DateOnly? ExpiryDate,
    DateOnly CurrentDate,
    DateTime UtcNow)
    : ICommand<CaregiverProfileResponse>;

public sealed class AddCertificateCommandValidator
    : AbstractValidator<AddCertificateCommand>
{
    public AddCertificateCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.Type).IsInEnum();
        RuleFor(c => c.FileKey).NotEmpty();
        RuleFor(c => c.ExpiryDate)
            .GreaterThanOrEqualTo(c => c.CurrentDate)
            .When(c => c.ExpiryDate.HasValue)
            .WithMessage("Certificate has already expired.");
    }
}

public sealed class AddCertificateCommandHandler
    : ICommandHandler<AddCertificateCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public AddCertificateCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        AddCertificateCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        if (caregiver.Type != CaregiverType.Medical)
        {
            return OnboardingErrors.InvalidCertificateOperation;
        }

        try
        {
            // Mandatory-unique, max five additional, unexpired rules
            // live on the aggregate.
            caregiver.AddCertificate(
                request.Type,
                request.FileKey,
                request.ExpiryDate,
                request.CurrentDate);
        }
        catch (DomainException)
        {
            return OnboardingErrors.InvalidCertificateOperation;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return caregiver.ToProfileResponse();
    }
}

// ----------------- Self-service: replace file ------------------------

public sealed record ReplaceCertificateFileCommand(
    UserId UserId,
    CaregiverCertificateId CertificateId,
    string FileKey,
    DateOnly? ExpiryDate,
    DateOnly CurrentDate,
    DateTime UtcNow)
    : ICommand<CaregiverProfileResponse>;

public sealed class ReplaceCertificateFileCommandValidator
    : AbstractValidator<ReplaceCertificateFileCommand>
{
    public ReplaceCertificateFileCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.CertificateId)
            .NotEqual(CaregiverCertificateId.Empty);
        RuleFor(c => c.FileKey).NotEmpty();
        RuleFor(c => c.ExpiryDate)
            .GreaterThanOrEqualTo(c => c.CurrentDate)
            .When(c => c.ExpiryDate.HasValue)
            .WithMessage("Certificate has already expired.");
    }
}

public sealed class ReplaceCertificateFileCommandHandler
    : ICommandHandler<ReplaceCertificateFileCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public ReplaceCertificateFileCommandHandler(
        ICaregiversDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        ReplaceCertificateFileCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        if (!caregiver.Certificates.Any(
                c => c.Id == request.CertificateId))
        {
            return OnboardingErrors.CertificateNotFound;
        }

        string? previousFileKey =
            caregiver.Certificates
                .Single(c => c.Id == request.CertificateId)
                .FilePath;

        try
        {
            // Mandatory replacement flips an Active caregiver back to
            // PendingReview and resets the certificate to Pending.
            caregiver.UpdateCertificateFile(
                request.CertificateId,
                request.FileKey,
                request.ExpiryDate,
                request.CurrentDate,
                request.UtcNow);
        }
        catch (DomainException)
        {
            return OnboardingErrors.InvalidCertificateOperation;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        // Best-effort orphan cleanup of the replaced scan.
        if (!string.IsNullOrWhiteSpace(previousFileKey) &&
            previousFileKey != request.FileKey)
        {
            await _fileStorage.DeleteAsync(
                previousFileKey,
                cancellationToken);
        }

        return caregiver.ToProfileResponse();
    }
}

// ------------------- Self-service: remove ----------------------------

public sealed record RemoveCertificateCommand(
    UserId UserId,
    CaregiverCertificateId CertificateId)
    : ICommand<CaregiverProfileResponse>;

public sealed class RemoveCertificateCommandValidator
    : AbstractValidator<RemoveCertificateCommand>
{
    public RemoveCertificateCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.CertificateId)
            .NotEqual(CaregiverCertificateId.Empty);
    }
}

public sealed class RemoveCertificateCommandHandler
    : ICommandHandler<RemoveCertificateCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public RemoveCertificateCommandHandler(
        ICaregiversDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<CaregiverProfileResponse>> Handle(
        RemoveCertificateCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.UserId == request.UserId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(c => c.Id == request.CertificateId);

        if (certificate is null)
        {
            return OnboardingErrors.CertificateNotFound;
        }

        string fileKey = certificate.FilePath;

        try
        {
            // Mandatory certificates (Practice License / Graduation)
            // cannot be removed — replace the file instead.
            caregiver.RemoveCertificate(request.CertificateId);
        }
        catch (DomainException)
        {
            return OnboardingErrors.InvalidCertificateOperation;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(fileKey))
        {
            await _fileStorage.DeleteAsync(
                fileKey,
                cancellationToken);
        }

        return caregiver.ToProfileResponse();
    }
}

// ---------------------- Admin: verification ---------------------------

public sealed record VerifyCertificateCommand(
    CaregiverId CaregiverId,
    CaregiverCertificateId CertificateId)
    : ICommand;

public sealed class VerifyCertificateCommandHandler
    : ICommandHandler<VerifyCertificateCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public VerifyCertificateCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        VerifyCertificateCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.NotFound);
        }

        if (!caregiver.Certificates.Any(
                c => c.Id == request.CertificateId))
        {
            return Result.Failure(
                OnboardingErrors.CertificateNotFound);
        }

        try
        {
            // Only a Pending certificate can be Verified.
            caregiver.VerifyCertificate(request.CertificateId);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidCertificateOperation);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record RejectCertificateCommand(
    CaregiverId CaregiverId,
    CaregiverCertificateId CertificateId,
    string Reason)
    : ICommand;

public sealed class RejectCertificateCommandValidator
    : AbstractValidator<RejectCertificateCommand>
{
    public RejectCertificateCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.CertificateId)
            .NotEqual(CaregiverCertificateId.Empty);
        RuleFor(c => c.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class RejectCertificateCommandHandler
    : ICommandHandler<RejectCertificateCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public RejectCertificateCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        RejectCertificateCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.NotFound);
        }

        if (!caregiver.Certificates.Any(
                c => c.Id == request.CertificateId))
        {
            return Result.Failure(
                OnboardingErrors.CertificateNotFound);
        }

        try
        {
            // Only a Pending certificate can be Rejected; mandatory
            // rejection also forces the caregiver Unavailable.
            caregiver.RejectCertificate(
                request.CertificateId,
                request.Reason.Trim());
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidCertificateOperation);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

public sealed record RevokeCertificateCommand(
    CaregiverId CaregiverId,
    CaregiverCertificateId CertificateId,
    string Reason,
    DateTime UtcNow)
    : ICommand;

public sealed class RevokeCertificateCommandValidator
    : AbstractValidator<RevokeCertificateCommand>
{
    public RevokeCertificateCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.CertificateId)
            .NotEqual(CaregiverCertificateId.Empty);
        RuleFor(c => c.Reason)
            .NotEmpty()
            .MaximumLength(500);
    }
}

public sealed class RevokeCertificateCommandHandler
    : ICommandHandler<RevokeCertificateCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public RevokeCertificateCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        RevokeCertificateCommand request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(
                OnboardingErrors.NotFound);
        }

        if (!caregiver.Certificates.Any(
                c => c.Id == request.CertificateId))
        {
            return Result.Failure(
                OnboardingErrors.CertificateNotFound);
        }

        try
        {
            // Only a Verified certificate can be Revoked; revoking a
            // mandatory certificate suspends an Active caregiver.
            caregiver.RevokeCertificate(
                request.CertificateId,
                request.Reason.Trim(),
                request.UtcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidCertificateOperation);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }
}

// ---------------------- Admin: file download --------------------------

public sealed record CertificateFileContent(
    string FileName,
    string ContentType,
    Stream Content);

public sealed record GetCertificateFileQuery(
    CaregiverId CaregiverId,
    CaregiverCertificateId CertificateId)
    : IQuery<CertificateFileContent>;

public sealed class GetCertificateFileQueryHandler
    : IQueryHandler<GetCertificateFileQuery, CertificateFileContent>
{
    private readonly ICaregiversDbContext _dbContext;
    private readonly IFileStorage _fileStorage;

    public GetCertificateFileQueryHandler(
        ICaregiversDbContext dbContext,
        IFileStorage fileStorage)
    {
        _dbContext = dbContext;
        _fileStorage = fileStorage;
    }

    public async Task<Result<CertificateFileContent>> Handle(
        GetCertificateFileQuery request,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            await _dbContext.Caregivers
                .AsNoTracking()
                .Include(c => c.Certificates)
                .SingleOrDefaultAsync(
                    c => c.Id == request.CaregiverId,
                    cancellationToken);

        if (caregiver is null)
        {
            return OnboardingErrors.NotFound;
        }

        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(c => c.Id == request.CertificateId);

        if (certificate is null)
        {
            return OnboardingErrors.CertificateNotFound;
        }

        Result<PrivateFileContent> file =
            await _fileStorage.OpenReadAsync(
                certificate.FilePath,
                cancellationToken);

        if (file.IsFailure)
        {
            return Result<CertificateFileContent>.Failure(
                file.Error);
        }

        string extension =
            file.Value.ContentType.ToLowerInvariant() switch
            {
                "application/pdf" => ".pdf",
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "image/webp" => ".webp",
                _ => ".bin"
            };

        string fileName =
            $"certificate-{certificate.Id.Value:N}{extension}";

        return new CertificateFileContent(
            fileName,
            file.Value.ContentType,
            file.Value.Content);
    }
}