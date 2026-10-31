using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Ahlena.BuildingBlocks.Application.Abstractions.Storage;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Infrastructure.Storage;
using Ahlena.Modules.Caregivers.Application.Onboarding;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Caregivers.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Caregivers.Onboarding;

public sealed class CaregiverCertificatesTests : IDisposable
{
    private static readonly DateOnly CurrentDate =
        new(2026, 9, 1);

    private readonly string _rootPath;
    private readonly LocalDiskFileStorage _storage;

    public CaregiverCertificatesTests()
    {
        _rootPath =
            Path.Combine(
                Path.GetTempPath(),
                "ahlena-certificate-tests",
                Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(_rootPath);

        _storage =
            new LocalDiskFileStorage(
                Options.Create(
                    new LocalStorageOptions
                    {
                        RootPath = _rootPath
                    }));
    }

    [Fact]
    public async Task AddCertificate_ShouldPersistPendingCertificate()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        string fileKey = await SavePdfAsync();

        var handler =
            new AddCertificateCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new AddCertificateCommand(
                    userId,
                    CaregiverCertificateType.PracticeLicense,
                    fileKey,
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Certificates);
        Assert.Equal(
            CertificateVerificationStatus.Pending,
            result.Value.Certificates[0].VerificationStatus);

        // The brand-new qualification starts with exactly one pending version.
        CertificateVersionItemResponse version =
            Assert.Single(
                result.Value.Certificates[0].Versions);
        Assert.Equal(
            CertificateVersionClassification.Pending,
            version.Classification);
    }

    [Fact]
    public async Task AddCertificate_ShouldRejectForCompanionCaregiver()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Companion);

        string fileKey = await SavePdfAsync();

        var handler =
            new AddCertificateCommandHandler(dbContext);

        var result =
            await handler.Handle(
                new AddCertificateCommand(
                    userId,
                    CaregiverCertificateType.PracticeLicense,
                    fileKey,
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidCertificateOperation,
            result.Error);
    }

    [Fact]
    public async Task AddCertificate_ShouldRejectDuplicatePracticeLicense()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var handler =
            new AddCertificateCommandHandler(dbContext);

        await handler.Handle(
            new AddCertificateCommand(
                userId,
                CaregiverCertificateType.PracticeLicense,
                await SavePdfAsync(),
                null,
                CurrentDate,
                DateTime.UtcNow),
            default);

        var duplicate =
            await handler.Handle(
                new AddCertificateCommand(
                    userId,
                    CaregiverCertificateType.PracticeLicense,
                    await SavePdfAsync(),
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        Assert.True(duplicate.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidCertificateOperation,
            duplicate.Error);
    }

    /// <summary>
    /// A replacement no longer physically deletes the previous scan: the old
    /// scan becomes retained history (its file stays readable, its version is
    /// visible), while the approved version REMAINS the effective current one
    /// and the new upload rides along as the pending candidate.
    /// </summary>
    [Fact]
    public async Task ReplaceCertificateFile_ShouldRetainPreviousFileAsHistory()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        string firstKey = await SavePdfAsync();

        var addHandler =
            new AddCertificateCommandHandler(dbContext);

        var added =
            await addHandler.Handle(
                new AddCertificateCommand(
                    userId,
                    CaregiverCertificateType.PracticeLicense,
                    firstKey,
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        await new VerifyCertificateCommandHandler(dbContext)
            .Handle(
                new VerifyCertificateCommand(
                    caregiverId,
                    certificateId),
                default);

        string replacementKey = await SavePdfAsync();

        var replaceHandler =
            new ReplaceCertificateFileCommandHandler(
                dbContext);

        var replaced =
            await replaceHandler.Handle(
                new ReplaceCertificateFileCommand(
                    userId,
                    certificateId,
                    replacementKey,
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        Assert.True(replaced.IsSuccess);
        // Approved contract: the valid approved certificate REMAINS the
        // effective current one while a replacement is pending (availability
        // is not suspended), so the effective snapshot status stays Verified.
        // The pending replacement is the candidate awaiting targeted review.
        Assert.Equal(
            CertificateVerificationStatus.Verified,
            replaced.Value.Certificates[0].VerificationStatus);

        // The previously approved scan is NOT deleted: its bytes remain.
        Result<PrivateFileContent> oldFile =
            await _storage.OpenReadAsync(firstKey);

        Assert.True(oldFile.IsSuccess);

        // Both the approved current version and the pending replacement are
        // visible, classified honestly.
        CertificateItemResponse certificate =
            Assert.Single(replaced.Value.Certificates);

        Assert.True(certificate.HasPendingReplacement);
        Assert.Contains(
            certificate.Versions,
            version =>
                version.Classification ==
                    CertificateVersionClassification.Current);
        Assert.Contains(
            certificate.Versions,
            version =>
                version.Classification ==
                    CertificateVersionClassification.Pending);
    }

    [Fact]
    public async Task RemoveCertificate_ShouldTombstoneAdditionalButRejectMandatory()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        UserId userId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var addHandler =
            new AddCertificateCommandHandler(dbContext);

        var mandatory =
            await addHandler.Handle(
                new AddCertificateCommand(
                    userId,
                    CaregiverCertificateType.PracticeLicense,
                    await SavePdfAsync(),
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        var additional =
            await addHandler.Handle(
                new AddCertificateCommand(
                    userId,
                    CaregiverCertificateType.AdditionalCertificate,
                    await SavePdfAsync(),
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        CaregiverCertificateId additionalId =
            additional.Value.Certificates
                .Single(c =>
                    c.Type ==
                    CaregiverCertificateType.AdditionalCertificate)
                .Id;

        var removeHandler =
            new RemoveCertificateCommandHandler(
                dbContext);

        var removedAdditional =
            await removeHandler.Handle(
                new RemoveCertificateCommand(
                    userId,
                    additionalId),
                default);

        Assert.True(removedAdditional.IsSuccess);

        // The tombstoned qualification leaves the live profile but is retained
        // in the audit-only removed list with its history versions intact.
        Assert.DoesNotContain(
            removedAdditional.Value.Certificates,
            c => c.Id == additionalId);
        Assert.Contains(
            removedAdditional.Value.RemovedCertificates,
            c => c.Id == additionalId);

        CaregiverCertificateId mandatoryId =
            mandatory.Value.Certificates[0].Id;

        var removedMandatory =
            await removeHandler.Handle(
                new RemoveCertificateCommand(
                    userId,
                    mandatoryId),
                default);

        Assert.True(removedMandatory.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidCertificateOperation,
            removedMandatory.Error);
    }

    [Fact]
    public async Task VerifyCertificate_ShouldMarkCertificateVerified()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.PracticeLicense,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        var result =
            await new VerifyCertificateCommandHandler(dbContext)
                .Handle(
                    new VerifyCertificateCommand(
                        caregiverId,
                        certificateId),
                    default);

        Assert.True(result.IsSuccess);

        var profile =
            await new GetCaregiverProfileQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverProfileQuery(userId),
                    default);

        CertificateItemResponse certificate =
            profile.Value.Certificates
                .Single(c => c.Id == certificateId);

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            certificate.VerificationStatus);

        CertificateVersionItemResponse version =
            Assert.Single(certificate.Versions);
        Assert.Equal(
            CertificateVersionClassification.Current,
            version.Classification);
    }

    [Fact]
    public async Task RevokeCertificate_ShouldRejectForPendingCertificate()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.PracticeLicense,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        var result =
            await new RevokeCertificateCommandHandler(dbContext)
                .Handle(
                    new RevokeCertificateCommand(
                        caregiverId,
                        certificateId,
                        "Document could not be authenticated.",
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidCertificateOperation,
            result.Error);
    }

    // ------------------- Per-version history removal --------------------

    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldSoftRemoveHistoryVersion()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid historyVersionId) =
            await CreateVerifiedThenSupersededAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new RemoveOwnCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveOwnCertificateVersionCommand(
                        userId,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            historyVersionId),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsSuccess);

        var profile =
            await new GetCaregiverProfileQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverProfileQuery(userId),
                    default);

        CertificateItemResponse certificate =
            profile.Value.Certificates
                .Single(c => c.Id == certificateId);

        // The removed history version is hidden from the normal version list,
        // but the current effective version and its file are untouched.
        Assert.DoesNotContain(
            certificate.Versions,
            version =>
                version.Id.Value == historyVersionId);

        CertificateVersionItemResponse current =
            Assert.Single(certificate.Versions);
        Assert.Equal(
            CertificateVersionClassification.Current,
            current.Classification);

        Result<CertificateFileContent> hiddenFile =
            await new GetCertificateFileQueryHandler(
                    dbContext,
                    _storage)
                .Handle(
                    new GetCertificateFileQuery(
                        CaregiverId.Empty,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            historyVersionId),
                        userId),
                    default);

        // A removed version's file is no longer downloadable (generic 404).
        Assert.True(hiddenFile.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotFound,
            hiddenFile.Error);
    }

    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldBeIdempotent()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid historyVersionId) =
            await CreateVerifiedThenSupersededAsync(
                dbContext,
                caregiverId,
                userId);

        var handler =
            new RemoveOwnCertificateVersionCommandHandler(dbContext);

        var command =
            new RemoveOwnCertificateVersionCommand(
                userId,
                certificateId,
                new CaregiverCertificateVersionId(historyVersionId),
                DateTime.UtcNow);

        Assert.True((await handler.Handle(command, default)).IsSuccess);
        Assert.True((await handler.Handle(command, default)).IsSuccess);
    }

    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldRejectCurrentVersion()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid currentVersionId) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new RemoveOwnCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveOwnCertificateVersionCommand(
                        userId,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            currentVersionId),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotRemovable,
            result.Error);
    }

    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldRejectLivePendingVersion()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid pendingVersionId) =
            await CreatePendingReplacementAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new RemoveOwnCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveOwnCertificateVersionCommand(
                        userId,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            pendingVersionId),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotRemovable,
            result.Error);
    }

    /// <summary>
    /// An initial rejected version (no approved current) backs the effective
    /// snapshot — its values are the only thing keeping the displayed status and
    /// the legacy snapshot download honest — so it is protected until a
    /// replacement supersedes it. Removing it is a stable 409, never a silent
    /// break of the no-version download.
    /// </summary>
    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldProtectInitialRejectedCandidate()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.AdditionalCertificate,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        Guid rejectedVersionId =
            added.Value.Certificates[0].Versions.Single().Id.Value;

        var rejected =
            await new RejectCertificateCommandHandler(dbContext)
                .Handle(
                    new RejectCertificateCommand(
                        caregiverId,
                        certificateId,
                        "Unreadable scan.",
                        new CaregiverCertificateVersionId(
                            rejectedVersionId),
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        Assert.True(rejected.IsSuccess);

        var result =
            await new RemoveOwnCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveOwnCertificateVersionCommand(
                        userId,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            rejectedVersionId),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotRemovable,
            result.Error);
    }

    /// <summary>
    /// A rejected version that a later pending upload superseded is genuine
    /// history: it can be removed, and the legacy no-version download then
    /// serves the superseding candidate, never the removed file's key.
    /// </summary>
    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldRemoveSupersededRejectedAndLegacyDownloadKeepsCandidate()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        byte[] rejectedPayload = [9, 9, 9, 9];
        byte[] candidatePayload = [7, 7, 7, 7];

        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.AdditionalCertificate,
                        await SavePdfAsync(rejectedPayload),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        Guid rejectedVersionId =
            added.Value.Certificates[0].Versions.Single().Id.Value;

        await new RejectCertificateCommandHandler(dbContext)
            .Handle(
                new RejectCertificateCommand(
                    caregiverId,
                    certificateId,
                    "Unreadable scan.",
                    new CaregiverCertificateVersionId(
                        rejectedVersionId),
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        // A replacement is uploaded: the rejected upload becomes superseded
        // history and the replacement is the live pending candidate.
        var replaced =
            await new ReplaceCertificateFileCommandHandler(dbContext)
                .Handle(
                    new ReplaceCertificateFileCommand(
                        userId,
                        certificateId,
                        await SavePdfAsync(candidatePayload),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        Assert.True(replaced.IsSuccess);

        var result =
            await new RemoveOwnCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveOwnCertificateVersionCommand(
                        userId,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            rejectedVersionId),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsSuccess);

        // The removed version's own target route is a generic 404.
        Result<CertificateFileContent> removedTarget =
            await new GetCertificateFileQueryHandler(
                    dbContext,
                    _storage)
                .Handle(
                    new GetCertificateFileQuery(
                        CaregiverId.Empty,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            rejectedVersionId),
                        userId),
                    default);

        Assert.True(removedTarget.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotFound,
            removedTarget.Error);

        // The legacy no-version download serves the superseding candidate, not
        // the removed key.
        Result<CertificateFileContent> legacy =
            await new GetCertificateFileQueryHandler(
                    dbContext,
                    _storage)
                .Handle(
                    new GetCertificateFileQuery(
                        CaregiverId.Empty,
                        certificateId,
                        VersionId: null,
                        OwnerUserId: userId),
                    default);

        Assert.True(legacy.IsSuccess);

        using MemoryStream buffer = new();
        await legacy.Value.Content.CopyToAsync(buffer);
        Assert.Equal(candidatePayload, buffer.ToArray());
    }

    /// <summary>
    /// The profile's per-version <c>CanRemove</c> must agree with the removal
    /// guard: the superseded history version is removable, the approved current
    /// is not — and a history version that still backs the effective snapshot
    /// (an initial rejection with no approved current) is not removable either.
    /// </summary>
    [Fact]
    public async Task Profile_ShouldExposeCanRemoveConsistentlyWithTheRemovalGuard()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid historyVersionId) =
            await CreateVerifiedThenSupersededAsync(
                dbContext,
                caregiverId,
                userId);

        var profile =
            await new GetCaregiverProfileQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverProfileQuery(userId),
                    default);

        CertificateItemResponse certificate =
            profile.Value.Certificates
                .Single(c => c.Id == certificateId);

        Assert.True(
            certificate.Versions
                .Single(version =>
                    version.Id.Value == historyVersionId)
                .CanRemove);

        Assert.False(
            certificate.Versions
                .Single(version =>
                    version.Classification ==
                        CertificateVersionClassification.Current)
                .CanRemove);
    }

    [Fact]
    public async Task Profile_ShouldMarkEffectiveRejectedCandidateNotRemovable()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.AdditionalCertificate,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        Guid rejectedVersionId =
            added.Value.Certificates[0].Versions.Single().Id.Value;

        await new RejectCertificateCommandHandler(dbContext)
            .Handle(
                new RejectCertificateCommand(
                    caregiverId,
                    certificateId,
                    "Unreadable scan.",
                    new CaregiverCertificateVersionId(
                        rejectedVersionId),
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        var profile =
            await new GetCaregiverProfileQueryHandler(dbContext)
                .Handle(
                    new GetCaregiverProfileQuery(userId),
                    default);

        CertificateVersionItemResponse version =
            profile.Value.Certificates
                .Single(c => c.Id == certificateId)
                .Versions
                .Single();

        Assert.Equal(
            CertificateVersionClassification.History,
            version.Classification);
        Assert.False(version.CanRemove);
    }

    [Fact]
    public async Task VerifyCertificate_ShouldReturnConflictForLegacyRouteWhenNothingPending()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, _) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        // The legacy no-version route keeps its historical 409 when the
        // qualification has nothing pending to verify.
        var result =
            await new VerifyCertificateCommandHandler(dbContext)
                .Handle(
                    new VerifyCertificateCommand(
                        caregiverId,
                        certificateId),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidCertificateOperation,
            result.Error);
    }

    [Fact]
    public async Task VerifyCertificate_ShouldReturnNotFoundForUnknownVersionTarget()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, _) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new VerifyCertificateCommandHandler(dbContext)
                .Handle(
                    new VerifyCertificateCommand(
                        caregiverId,
                        certificateId,
                        CaregiverCertificateVersionId.New()),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotFound,
            result.Error);
    }

    [Fact]
    public async Task RejectCertificate_ShouldReturnConflictForLegacyRouteWhenNothingPending()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, _) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new RejectCertificateCommandHandler(dbContext)
                .Handle(
                    new RejectCertificateCommand(
                        caregiverId,
                        certificateId,
                        "No pending upload.",
                        VersionId: null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.InvalidCertificateOperation,
            result.Error);
    }

    [Fact]
    public async Task RejectCertificate_ShouldReturnNotFoundForUnknownVersionTarget()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, _) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new RejectCertificateCommandHandler(dbContext)
                .Handle(
                    new RejectCertificateCommand(
                        caregiverId,
                        certificateId,
                        "No such upload.",
                        CaregiverCertificateVersionId.New(),
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotFound,
            result.Error);
    }

    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldReturnNotFoundForUnknownVersion()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, _) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new RemoveOwnCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveOwnCertificateVersionCommand(
                        userId,
                        certificateId,
                        CaregiverCertificateVersionId.New(),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotFound,
            result.Error);
    }

    [Fact]
    public async Task RemoveOwnCertificateVersion_ShouldNotLeakAnotherCaregiversVersion()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid historyVersionId) =
            await CreateVerifiedThenSupersededAsync(
                dbContext,
                caregiverId,
                userId);

        UserId otherUserId = await BootstrapAsync(
            dbContext,
            CaregiverType.Medical);

        var result =
            await new RemoveOwnCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveOwnCertificateVersionCommand(
                        otherUserId,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            historyVersionId),
                        DateTime.UtcNow),
                    default);

        // The other caregiver cannot resolve the owner's certificate at all:
        // a generic 404, never a 403 that would confirm it exists.
        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateNotFound,
            result.Error);
    }

    [Fact]
    public async Task RemoveAdminCertificateVersion_ShouldRemoveScopedHistoryVersion()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid historyVersionId) =
            await CreateVerifiedThenSupersededAsync(
                dbContext,
                caregiverId,
                userId);

        UserId actor = UserId.New();

        var result =
            await new RemoveAdminCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveAdminCertificateVersionCommand(
                        caregiverId,
                        actor,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            historyVersionId),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsSuccess);

        CaregiverCertificateVersion? removed =
            dbContext.ChangeTracker
                .Entries<CaregiverCertificateVersion>()
                .Select(entry => entry.Entity)
                .SingleOrDefault(version =>
                    version.Id.Value == historyVersionId);

        Assert.NotNull(removed);
        Assert.True(removed!.IsRemoved);
        Assert.Equal(actor, removed.RemovedByUserId);
    }

    [Fact]
    public async Task RemoveAdminCertificateVersion_ShouldRejectCurrentVersion()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid currentVersionId) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new RemoveAdminCertificateVersionCommandHandler(
                    dbContext)
                .Handle(
                    new RemoveAdminCertificateVersionCommand(
                        caregiverId,
                        UserId.New(),
                        certificateId,
                        new CaregiverCertificateVersionId(
                            currentVersionId),
                        DateTime.UtcNow),
                    default);

        Assert.True(result.IsFailure);
        Assert.Equal(
            OnboardingErrors.CertificateVersionNotRemovable,
            result.Error);
    }

    [Fact]
    public async Task GetCertificateFile_ShouldStreamSavedContent()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.PracticeLicense,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        var result =
            await new GetCertificateFileQueryHandler(
                    dbContext,
                    _storage)
                .Handle(
                    new GetCertificateFileQuery(
                        caregiverId,
                        certificateId),
                    default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "application/pdf",
            result.Value.ContentType);

        using MemoryStream buffer = new();
        await result.Value.Content.CopyToAsync(buffer);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, buffer.ToArray());
    }

    [Fact]
    public async Task GetCertificateFile_ShouldServeNamedVersionForOwner()
    {
        using CaregiversDbContext dbContext =
            CreateDbContext();

        var (caregiverId, userId) =
            await BootstrapMedicalAsync(dbContext);

        (CaregiverCertificateId certificateId, Guid currentVersionId) =
            await CreateVerifiedAsync(
                dbContext,
                caregiverId,
                userId);

        var result =
            await new GetCertificateFileQueryHandler(
                    dbContext,
                    _storage)
                .Handle(
                    new GetCertificateFileQuery(
                        CaregiverId.Empty,
                        certificateId,
                        new CaregiverCertificateVersionId(
                            currentVersionId),
                        userId),
                    default);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            "application/pdf",
            result.Value.ContentType);
    }

    // ----------------------------- Helpers -------------------------------

    /// <summary>
    /// Produces a qualification with one retained-history version (the first,
    /// superseded upload) and one current version (the approved replacement).
    /// Returns the current history version id.
    /// </summary>
    private async Task<(CaregiverCertificateId CertificateId, Guid HistoryVersionId)>
        CreateVerifiedThenSupersededAsync(
            CaregiversDbContext dbContext,
            CaregiverId caregiverId,
            UserId userId)
    {
        var addHandler =
            new AddCertificateCommandHandler(dbContext);

        var added =
            await addHandler.Handle(
                new AddCertificateCommand(
                    userId,
                    CaregiverCertificateType.PracticeLicense,
                    await SavePdfAsync(),
                    null,
                    CurrentDate,
                    DateTime.UtcNow),
                default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;
        Guid firstVersionId =
            added.Value.Certificates[0].Versions.Single().Id.Value;

        await new VerifyCertificateCommandHandler(dbContext)
            .Handle(
                new VerifyCertificateCommand(
                    caregiverId,
                    certificateId),
                default);

        var replaced =
            await new ReplaceCertificateFileCommandHandler(dbContext)
                .Handle(
                    new ReplaceCertificateFileCommand(
                        userId,
                        certificateId,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        Guid pendingVersionId =
            replaced.Value.Certificates[0].Versions
                .Single(version =>
                    version.Classification ==
                        CertificateVersionClassification.Pending)
                .Id.Value;

        await new VerifyCertificateCommandHandler(dbContext)
            .Handle(
                new VerifyCertificateCommand(
                    caregiverId,
                    certificateId,
                    new CaregiverCertificateVersionId(
                        pendingVersionId)),
                default);

        return (certificateId, firstVersionId);
    }

    /// <summary>Adds and verifies a qualification, returning its current id.</summary>
    private async Task<(CaregiverCertificateId CertificateId, Guid VersionId)>
        CreateVerifiedAsync(
            CaregiversDbContext dbContext,
            CaregiverId caregiverId,
            UserId userId)
    {
        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.PracticeLicense,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;
        Guid versionId =
            added.Value.Certificates[0].Versions.Single().Id.Value;

        await new VerifyCertificateCommandHandler(dbContext)
            .Handle(
                new VerifyCertificateCommand(
                    caregiverId,
                    certificateId),
                default);

        return (certificateId, versionId);
    }

    /// <summary>Adds a qualification then a replacement, returning the pending id.</summary>
    private async Task<(CaregiverCertificateId CertificateId, Guid VersionId)>
        CreatePendingReplacementAsync(
            CaregiversDbContext dbContext,
            CaregiverId caregiverId,
            UserId userId)
    {
        var added =
            await new AddCertificateCommandHandler(dbContext)
                .Handle(
                    new AddCertificateCommand(
                        userId,
                        CaregiverCertificateType.PracticeLicense,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        CaregiverCertificateId certificateId =
            added.Value.Certificates[0].Id;

        var replaced =
            await new ReplaceCertificateFileCommandHandler(dbContext)
                .Handle(
                    new ReplaceCertificateFileCommand(
                        userId,
                        certificateId,
                        await SavePdfAsync(),
                        null,
                        CurrentDate,
                        DateTime.UtcNow),
                    default);

        Guid pendingVersionId =
            replaced.Value.Certificates[0].Versions
                .Single(version =>
                    version.Classification ==
                        CertificateVersionClassification.Pending)
                .Id.Value;

        return (certificateId, pendingVersionId);
    }

    private async Task<string> SavePdfAsync(byte[]? payload = null)
    {
        payload ??= [1, 2, 3, 4];

        using MemoryStream content = new(payload);

        Result<StoredFile> saved =
            await _storage.SavePrivateAsync(
                content,
                "application/pdf",
                payload.Length,
                CertificateStorage.Folder,
                CancellationToken.None);

        return saved.Value.Key;
    }

    private static async Task<UserId> BootstrapAsync(
        CaregiversDbContext dbContext,
        CaregiverType caregiverType)
    {
        UserId userId = UserId.New();

        await new BootstrapCaregiverCommandHandler(dbContext)
            .Handle(
                new BootstrapCaregiverCommand(
                    userId,
                    caregiverType),
                default);

        return userId;
    }

    private static async Task<(CaregiverId CaregiverId, UserId UserId)>
        BootstrapMedicalAsync(CaregiversDbContext dbContext)
    {
        UserId userId = UserId.New();

        var bootstrap =
            await new BootstrapCaregiverCommandHandler(dbContext)
                .Handle(
                    new BootstrapCaregiverCommand(
                        userId,
                        CaregiverType.Medical),
                    default);

        return (bootstrap.Value.Id, userId);
    }

    private static CaregiversDbContext CreateDbContext()
    {
        DbContextOptions<CaregiversDbContext> options =
            new DbContextOptionsBuilder<CaregiversDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CaregiversDbContext(options);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
