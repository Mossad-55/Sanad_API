using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Caregivers.Application.Abstractions.Data;
using Ahlena.Modules.Caregivers.Domain.Caregivers;

namespace Ahlena.Modules.Caregivers.Application.Onboarding;

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

        return caregiver.ToProfileResponse(request.CurrentDate);
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

/// <summary>
/// Appends a replacement upload as a new pending version. The previous scan is
/// retained as history for audit (never deleted); only the effective current
/// version keeps backing the caregiver's qualification until the replacement is
/// approved. When a valid approved mandatory version is already in force, the
/// caregiver's status and availability are left unchanged.
/// </summary>
public sealed class ReplaceCertificateFileCommandHandler
    : ICommandHandler<ReplaceCertificateFileCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public ReplaceCertificateFileCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
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

        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(c => c.Id == request.CertificateId);

        if (certificate is null || certificate.IsRemoved)
        {
            return OnboardingErrors.CertificateNotFound;
        }

        try
        {
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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OnboardingErrors.CertificateConflict;
        }

        return caregiver.ToProfileResponse(request.CurrentDate);
    }
}

// ------------------- Self-service: remove whole ----------------------

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

/// <summary>
/// Logically removes (tombstones) one whole Additional qualification. The row
/// and its version files stay retained for authorized history; only the active
/// profile, the Additional cap and public discovery drop it. Mandatory
/// qualifications still cannot be removed.
/// </summary>
public sealed class RemoveCertificateCommandHandler
    : ICommandHandler<RemoveCertificateCommand, CaregiverProfileResponse>
{
    private readonly ICaregiversDbContext _dbContext;

    public RemoveCertificateCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
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

        try
        {
            caregiver.RemoveCertificate(request.CertificateId);
        }
        catch (DomainException)
        {
            return OnboardingErrors.InvalidCertificateOperation;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return OnboardingErrors.CertificateConflict;
        }

        return caregiver.ToProfileResponse();
    }
}

// ------------- Self/Admin: remove one history version ----------------

public sealed record RemoveOwnCertificateVersionCommand(
    UserId UserId,
    CaregiverCertificateId CertificateId,
    CaregiverCertificateVersionId VersionId,
    DateTime UtcNow)
    : ICommand;

public sealed class RemoveOwnCertificateVersionCommandValidator
    : AbstractValidator<RemoveOwnCertificateVersionCommand>
{
    public RemoveOwnCertificateVersionCommandValidator()
    {
        RuleFor(c => c.UserId).NotEqual(UserId.Empty);
        RuleFor(c => c.CertificateId)
            .NotEqual(CaregiverCertificateId.Empty);
        RuleFor(c => c.VersionId)
            .NotEqual(CaregiverCertificateVersionId.Empty);
    }
}

public sealed class RemoveOwnCertificateVersionCommandHandler
    : ICommandHandler<RemoveOwnCertificateVersionCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public RemoveOwnCertificateVersionCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Result> Handle(
        RemoveOwnCertificateVersionCommand request,
        CancellationToken cancellationToken) =>
        SharedCertificateVersionRemoval.HandleAsync(
            _dbContext,
            request.UserId,
            request.UserId,
            caregiverId: null,
            request.CertificateId,
            request.VersionId,
            request.UtcNow,
            cancellationToken);
}

public sealed record RemoveAdminCertificateVersionCommand(
    CaregiverId CaregiverId,
    UserId ActorUserId,
    CaregiverCertificateId CertificateId,
    CaregiverCertificateVersionId VersionId,
    DateTime UtcNow)
    : ICommand;

public sealed class RemoveAdminCertificateVersionCommandValidator
    : AbstractValidator<RemoveAdminCertificateVersionCommand>
{
    public RemoveAdminCertificateVersionCommandValidator()
    {
        RuleFor(c => c.CaregiverId).NotEqual(CaregiverId.Empty);
        RuleFor(c => c.ActorUserId).NotEqual(UserId.Empty);
        RuleFor(c => c.CertificateId)
            .NotEqual(CaregiverCertificateId.Empty);
        RuleFor(c => c.VersionId)
            .NotEqual(CaregiverCertificateVersionId.Empty);
    }
}

public sealed class RemoveAdminCertificateVersionCommandHandler
    : ICommandHandler<RemoveAdminCertificateVersionCommand>
{
    private readonly ICaregiversDbContext _dbContext;

    public RemoveAdminCertificateVersionCommandHandler(
        ICaregiversDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<Result> Handle(
        RemoveAdminCertificateVersionCommand request,
        CancellationToken cancellationToken) =>
        SharedCertificateVersionRemoval.HandleAsync(
            _dbContext,
            ownerUserId: null,
            request.ActorUserId,
            request.CaregiverId,
            request.CertificateId,
            request.VersionId,
            request.UtcNow,
            cancellationToken);
}

/// <summary>
/// Shared history-version removal: resolves the caregiver by the authenticated
/// owner (self) or by id (admin), re-derives the version classification on the
/// freshly loaded aggregate, and refuses (409) any current / pending / unknown
/// target. A repeated removal of an already-removed version is an idempotent
/// success; a version that is not part of this caregiver's qualification is a
/// 404 (no existence leak across scopes). The removal is attributed to the
/// explicit acting user: the caregiver's own user id for a self removal, the
/// authenticated admin's user id for an admin removal — never the client body.
/// </summary>
internal static class SharedCertificateVersionRemoval
{
    internal static async Task<Result> HandleAsync(
        ICaregiversDbContext dbContext,
        UserId? ownerUserId,
        UserId actorUserId,
        CaregiverId? caregiverId,
        CaregiverCertificateId certificateId,
        CaregiverCertificateVersionId versionId,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        Caregiver? caregiver =
            ownerUserId.HasValue
                ? await dbContext.Caregivers
                    .Include(c => c.Certificates)
                    .SingleOrDefaultAsync(
                        c => c.UserId == ownerUserId.Value,
                        cancellationToken)
                : await dbContext.Caregivers
                    .Include(c => c.Certificates)
                    .SingleOrDefaultAsync(
                        c => c.Id == caregiverId!.Value,
                        cancellationToken);

        if (caregiver is null)
        {
            return Result.Failure(OnboardingErrors.NotFound);
        }

        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(c => c.Id == certificateId);

        if (certificate is null)
        {
            return Result.Failure(
                OnboardingErrors.CertificateNotFound);
        }

        CaregiverCertificateVersion? version =
            certificate.Versions
                .SingleOrDefault(candidate => candidate.Id == versionId);

        if (version is null)
        {
            return Result.Failure(
                OnboardingErrors.CertificateVersionNotFound);
        }

        // Idempotent: a repeated removal of an already-removed version is a
        // success and changes nothing.
        if (version.IsRemoved)
        {
            return Result.Success();
        }

        // Only genuinely superseded history may be removed. The live current
        // effective version, the pending review candidate and the version still
        // backing the effective snapshot (an initial rejection with no approved
        // current) are all refused with a stable 409, so the displayed status
        // and the legacy snapshot download never point at a removed file.
        if (!certificate.CanRemoveVersion(version))
        {
            return Result.Failure(
                OnboardingErrors.CertificateVersionNotRemovable);
        }

        try
        {
            caregiver.RemoveCertificateVersion(
                certificateId,
                versionId,
                actorUserId,
                utcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.CertificateVersionNotRemovable);
        }

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                OnboardingErrors.CertificateConflict);
        }

        return Result.Success();
    }
}

// ---------------------- Admin: verification ---------------------------

public sealed record VerifyCertificateCommand(
    CaregiverId CaregiverId,
    CaregiverCertificateId CertificateId,
    CaregiverCertificateVersionId? VersionId = null,
    DateTime? UtcNow = null)
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

        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(c => c.Id == request.CertificateId);

        if (certificate is null)
        {
            return Result.Failure(
                OnboardingErrors.CertificateNotFound);
        }

        DateTime utcNow = request.UtcNow ?? DateTime.UtcNow;

        try
        {
            // The WHOLE promotion runs in one transaction committed exactly
            // once: the lazy legacy materialization (whose INSERT lands in the
            // first save), the demote (save 1) and the promote (save 2). If the
            // second save, a concurrent writer or cancellation fails, the
            // demotion rolls back with it, so the qualification keeps its
            // previous approved current version instead of being left with zero
            // current versions and a still-Verified row.
            return await _dbContext.ExecuteInTransactionAsync(
                async operationToken =>
                {
                    // The reviewed version is the explicitly named one or the
                    // newest live pending version. The classification is
                    // recomputed on the freshly loaded aggregate, never trusted
                    // from the client. A legacy row is materialized first so its
                    // pending version has a stable identity.
                    caregiver.EnsureCertificateVersionsMaterialized(
                        request.CertificateId);

                    CaregiverCertificateVersion? target =
                        request.VersionId.HasValue
                            ? certificate.Versions.SingleOrDefault(
                                version =>
                                    version.Id == request.VersionId.Value)
                            : certificate.PendingVersion;

                    if (target is null)
                    {
                        // An explicit but unknown version id is a 404; the
                        // legacy no-version route keeps its historical 409 when
                        // nothing is pending to verify (pre-versioning parity).
                        return Result.Failure(
                            request.VersionId.HasValue
                                ? OnboardingErrors.CertificateVersionNotFound
                                : OnboardingErrors
                                    .InvalidCertificateOperation);
                    }

                    if (target.IsRemoved || target.IsHistory ||
                        target.VerificationStatus !=
                            CertificateVerificationStatus.Pending)
                    {
                        return Result.Failure(
                            OnboardingErrors.InvalidCertificateOperation);
                    }

                    // The demote is persisted before the promote so the
                    // partial-unique "one current version" index never sees two
                    // current rows; a concurrent edit loses the Revision race
                    // and both saves share one commit.
                    caregiver.BeginCertificatePromotion(
                        request.CertificateId,
                        target.Id,
                        utcNow);

                    await _dbContext.SaveChangesAsync(operationToken);

                    caregiver.CompleteCertificatePromotion(
                        request.CertificateId,
                        target.Id,
                        utcNow);

                    await _dbContext.SaveChangesAsync(operationToken);

                    return Result.Success();
                },
                cancellationToken);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidCertificateOperation);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                OnboardingErrors.CertificateConflict);
        }
    }
}

public sealed record RejectCertificateCommand(
    CaregiverId CaregiverId,
    CaregiverCertificateId CertificateId,
    string Reason,
    CaregiverCertificateVersionId? VersionId = null,
    DateOnly? CurrentDate = null,
    DateTime? UtcNow = null)
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

        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(c => c.Id == request.CertificateId);

        if (certificate is null)
        {
            return Result.Failure(
                OnboardingErrors.CertificateNotFound);
        }

        // A legacy row is materialized first so its pending version has a
        // stable identity before the reject is classified.
        caregiver.EnsureCertificateVersionsMaterialized(
            request.CertificateId);

        CaregiverCertificateVersion? target =
            request.VersionId.HasValue
                ? certificate.Versions.SingleOrDefault(
                    version => version.Id == request.VersionId.Value)
                : certificate.PendingVersion;

        if (target is null)
        {
            // An explicit but unknown version id is a 404; the legacy
            // no-version route keeps its historical 409 when nothing is pending
            // to reject (pre-versioning parity).
            return Result.Failure(
                request.VersionId.HasValue
                    ? OnboardingErrors.CertificateVersionNotFound
                    : OnboardingErrors.InvalidCertificateOperation);
        }

        if (target.IsRemoved || target.IsHistory ||
            target.VerificationStatus !=
                CertificateVerificationStatus.Pending)
        {
            return Result.Failure(
                OnboardingErrors.InvalidCertificateOperation);
        }

        DateTime utcNow = request.UtcNow ?? DateTime.UtcNow;

        DateOnly currentDate =
            request.CurrentDate ??
            CaregiverCertificateClock.ToCairoDate(utcNow);

        try
        {
            // Rejecting a pending replacement keeps any previously approved
            // mandatory qualification in force; only when no valid approved
            // version remains does the mandatory-safety consequence apply.
            caregiver.RejectCertificate(
                request.CertificateId,
                request.VersionId,
                request.Reason.Trim(),
                currentDate,
                utcNow);
        }
        catch (DomainException)
        {
            return Result.Failure(
                OnboardingErrors.InvalidCertificateOperation);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                OnboardingErrors.CertificateConflict);
        }

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

        CaregiverCertificate? certificate =
            caregiver.Certificates
                .SingleOrDefault(c => c.Id == request.CertificateId);

        if (certificate is null)
        {
            return Result.Failure(
                OnboardingErrors.CertificateNotFound);
        }

        try
        {
            // Only a Verified current version can be Revoked; revoking a
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

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(
                OnboardingErrors.CertificateConflict);
        }

        return Result.Success();
    }
}

// ---------------------- File download ---------------------------------

public sealed record CertificateFileContent(
    string FileName,
    string ContentType,
    Stream Content);

/// <summary>
/// Streams one stored certificate scan. The admin review surface resolves the
/// caregiver by id; the self-service surface resolves it by the authenticated
/// owner's user id. When <see cref="VersionId"/> is supplied the named
/// version's scan is served (after refusing a removed version); otherwise the
/// effective snapshot version (approved current, else live pending, else the
/// newest retained candidate) is served for backward compatibility, and that
/// version is protected from removal so a removed key is never served.
/// </summary>
public sealed record GetCertificateFileQuery(
    CaregiverId CaregiverId,
    CaregiverCertificateId CertificateId,
    CaregiverCertificateVersionId? VersionId = null,
    UserId? OwnerUserId = null)
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
            request.OwnerUserId.HasValue
                ? await _dbContext.Caregivers
                    .AsNoTracking()
                    .Include(c => c.Certificates)
                    .SingleOrDefaultAsync(
                        c => c.UserId == request.OwnerUserId.Value,
                        cancellationToken)
                : await _dbContext.Caregivers
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

        // A tombstoned qualification is invisible to its owner; an admin
        // audit read may still resolve it.
        if (certificate is null ||
            (request.OwnerUserId.HasValue && certificate.IsRemoved))
        {
            return OnboardingErrors.CertificateNotFound;
        }

        string? fileKey;

        if (request.VersionId.HasValue)
        {
            CaregiverCertificateVersion? version =
                certificate.Versions.SingleOrDefault(
                    candidate => candidate.Id == request.VersionId.Value);

            // A removed (or unknown) version is never downloadable.
            if (version is null || version.IsRemoved)
            {
                return OnboardingErrors.CertificateVersionNotFound;
            }

            fileKey = version.FilePath;
        }
        else
        {
            // The legacy no-version download resolves the version that backs
            // the effective snapshot (the approved current, else the live
            // pending candidate, else the newest retained candidate). That
            // version is protected from removal, so this key is never a removed
            // version's key; a legacy row with no materialized version serves
            // its own stored row key.
            CaregiverCertificateVersion? effective =
                certificate.EffectiveSnapshotVersion;

            if (effective is not null && effective.IsRemoved)
            {
                return OnboardingErrors.CertificateVersionNotFound;
            }

            fileKey = effective?.FilePath ?? certificate.FilePath;
        }

        Result<PrivateFileContent> file =
            await _fileStorage.OpenReadAsync(
                fileKey,
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
