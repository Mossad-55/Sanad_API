using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Infrastructure.Storage;
using Sanad.Modules.Caregivers.Application.Onboarding;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Caregivers.Infrastructure.Persistence;

namespace Sanad.UnitTests.Caregivers.Onboarding;

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
                "sanad-certificate-tests",
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

    [Fact]
    public async Task ReplaceCertificateFile_ShouldResetToPendingAndDeletePreviousFile()
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
                dbContext,
                _storage);

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
        Assert.Equal(
            CertificateVerificationStatus.Pending,
            replaced.Value.Certificates[0].VerificationStatus);

        Result<PrivateFileContent> oldFile =
            await _storage.OpenReadAsync(firstKey);

        Assert.True(oldFile.IsFailure);
    }

    [Fact]
    public async Task RemoveCertificate_ShouldAllowAdditionalButRejectMandatory()
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
                dbContext,
                _storage);

        var removedAdditional =
            await removeHandler.Handle(
                new RemoveCertificateCommand(
                    userId,
                    additionalId),
                default);

        Assert.True(removedAdditional.IsSuccess);
        Assert.DoesNotContain(
            removedAdditional.Value.Certificates,
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

        Assert.Equal(
            CertificateVerificationStatus.Verified,
            profile.Value.Certificates
                .Single(c => c.Id == certificateId)
                .VerificationStatus);
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

    private async Task<string> SavePdfAsync()
    {
        byte[] payload = [1, 2, 3, 4];

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