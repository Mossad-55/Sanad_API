using System.Text;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeDocumentUploadTests
{
    [Fact]
    public async Task Upload_RequiresDraftAndMatchingFileSignature_ThenPersistsPrivateMetadata()
    {
        using CareHomesDbContext db = new(new DbContextOptionsBuilder<CareHomesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        UserId owner = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        var saved = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        var storage = new RecordingStorage();
        var handler = new UploadCareHomeDocumentCommandHandler(db, storage);
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.7\nprivate fixture");

        var rejectedSignature = await handler.Handle(new UploadCareHomeDocumentCommand(
            owner, saved.Value.Version, CareHomeDocumentType.OperatingLicense, null,
            "application/pdf", pdf.Length, new MemoryStream(Encoding.ASCII.GetBytes("not a pdf"))), default);
        var uploaded = await handler.Handle(new UploadCareHomeDocumentCommand(
            owner, saved.Value.Version, CareHomeDocumentType.OperatingLicense, new DateOnly(2030, 1, 1),
            "application/pdf", pdf.Length, new MemoryStream(pdf)), default);

        Assert.False(rejectedSignature.IsSuccess);
        Assert.Equal("CareHomes.Document.InvalidContent", rejectedSignature.Error.Code);
        Assert.True(uploaded.IsSuccess);
        Assert.Equal(CareHomeDocumentStatus.PendingReview, uploaded.Value.Status);
        Assert.Equal(new DateOnly(2030, 1, 1), uploaded.Value.ExpiryDate);
        Assert.Equal(1, storage.SavedFiles);
        db.ChangeTracker.Clear();
        CareHomeDocument persisted = await db.Facilities.Include(x => x.Documents)
            .SelectMany(x => x.Documents).SingleAsync();
        Assert.Equal("private/care-home-documents/fixture.pdf", persisted.PrivateStorageKey);
    }

    private sealed class RecordingStorage : IFileStorage
    {
        public int SavedFiles { get; private set; }

        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default)
        {
            SavedFiles++;
            return Task.FromResult<Result<StoredFile>>(new StoredFile("private/care-home-documents/fixture.pdf"));
        }

        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult<Result<PrivateFileContent>>(Result<PrivateFileContent>.Failure(new Error("Storage.File.NotFound", "missing")));

        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(Result.Success());
    }
}
