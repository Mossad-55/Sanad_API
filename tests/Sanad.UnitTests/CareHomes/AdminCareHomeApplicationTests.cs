using System.Text;
using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Abstractions.Storage;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Application.Facilities;
using Sanad.Modules.CareHomes.Domain.Facilities;
using Sanad.Modules.CareHomes.Infrastructure.Persistence;

namespace Sanad.UnitTests.CareHomes;

public sealed class AdminCareHomeApplicationTests
{
    [Fact]
    public async Task AdminQueries_RejectInvalidPagingAndReturnTypedMissingIdErrors()
    {
        using CareHomesDbContext db = CreateDb();
        var list = new GetAdminCareHomeApplicationsQueryHandler(db);
        var detail = new GetAdminCareHomeApplicationQueryHandler(db);

        var invalidPage = await list.Handle(new GetAdminCareHomeApplicationsQuery(0, 20), default);
        var invalidSize = await list.Handle(new GetAdminCareHomeApplicationsQuery(1, 101), default);
        var invalidStatus = await list.Handle(new GetAdminCareHomeApplicationsQuery(1, 20, (CareHomeStatus)999), default);
        var missing = await detail.Handle(new GetAdminCareHomeApplicationQuery(Guid.NewGuid()), default);

        Assert.Equal("CareHomes.Admin.InvalidQuery", invalidPage.Error.Code);
        Assert.Equal("CareHomes.Admin.InvalidQuery", invalidSize.Error.Code);
        Assert.Equal("CareHomes.Admin.InvalidQuery", invalidStatus.Error.Code);
        Assert.Equal("CareHomes.Admin.ApplicationNotFound", missing.Error.Code);
    }

    [Fact]
    public async Task DocumentReview_RequiresCurrentVersionAndExplicitNonExpiringConfirmation()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = await CreateSubmittedFacility(db, owner);
        CareHomeDocument document = facility.Documents.Single(x => x.Type == CareHomeDocumentType.OperatingLicense);
        var handler = new ReviewCareHomeDocumentCommandHandler(db);
        int submittedVersion = facility.Version;

        var missingConfirmation = await handler.Handle(new ReviewCareHomeDocumentCommand(
            admin, facility.Id.Value, document.Id, submittedVersion, true, null, false, null), default);
        var verified = await handler.Handle(new ReviewCareHomeDocumentCommand(
            admin, facility.Id.Value, document.Id, submittedVersion, true, null, true, null), default);
        var stale = await handler.Handle(new ReviewCareHomeDocumentCommand(
            admin, facility.Id.Value, document.Id, submittedVersion, true, null, true, null), default);

        Assert.Equal("CareHomes.Admin.InvalidOperation", missingConfirmation.Error.Code);
        Assert.True(verified.IsSuccess);
        Assert.Equal(CareHomeDocumentStatus.Verified, verified.Value.Documents.Single(x => x.Id == document.Id).Status);
        Assert.Equal("CareHomes.Admin.Conflict", stale.Error.Code);
    }

    [Fact]
    public async Task DocumentReview_RejectsWithReasonAndMissingDocumentIsNotFound()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = await CreateSubmittedFacility(db, owner);
        CareHomeDocument document = facility.Documents.Single(x => x.Type == CareHomeDocumentType.Registration);
        var handler = new ReviewCareHomeDocumentCommandHandler(db);

        var rejected = await handler.Handle(new ReviewCareHomeDocumentCommand(
            admin, facility.Id.Value, document.Id, facility.Version, false, null, false, "Unreadable registration"), default);
        var missing = await handler.Handle(new ReviewCareHomeDocumentCommand(
            admin, facility.Id.Value, Guid.NewGuid(), facility.Version, false, null, false, "missing"), default);

        Assert.True(rejected.IsSuccess);
        var response = rejected.Value.Documents.Single(x => x.Id == document.Id);
        Assert.Equal(CareHomeDocumentStatus.Rejected, response.Status);
        Assert.Equal("Unreadable registration", response.ReviewReason);
        Assert.Equal("CareHomes.Admin.InvalidOperation", missing.Error.Code);
    }

    [Fact]
    public async Task ApplicationReview_SupportsCorrectionApprovalSuspensionAndReactivationWithVersionChecks()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        var facility = await CreateSubmittedFacility(db, owner);
        var handler = new ReviewCareHomeApplicationCommandHandler(db);

        var correction = await handler.Handle(new ReviewCareHomeApplicationCommand(
            admin, facility.Id.Value, facility.Version, CareHomeReviewAction.CorrectionsRequested, "Please replace certificate"), default);

        facility = await CreateSubmittedFacility(db, UserId.New());
        foreach (CareHomeDocument document in facility.Documents.ToArray())
        {
            facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
                .SingleAsync(x => x.Id == facility.Id);
            await new ReviewCareHomeDocumentCommandHandler(db).Handle(new ReviewCareHomeDocumentCommand(
                admin, facility.Id.Value, document.Id, facility.Version, true, null, true, null), default);
        }
        facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents).SingleAsync(x => x.Id == facility.Id);
        var approved = await handler.Handle(new ReviewCareHomeApplicationCommand(
            admin, facility.Id.Value, facility.Version, CareHomeReviewAction.Approved, null), default);
        var suspended = await handler.Handle(new ReviewCareHomeApplicationCommand(
            admin, facility.Id.Value, approved.Value.Version, CareHomeReviewAction.Suspended, "License alert"), default);
        var reactivated = await handler.Handle(new ReviewCareHomeApplicationCommand(
            admin, facility.Id.Value, suspended.Value.Version, CareHomeReviewAction.Reactivated, null), default);

        Assert.Equal(CareHomeStatus.NeedsCorrection, correction.Value.Status);
        Assert.Equal(CareHomeStatus.Approved, approved.Value.Status);
        Assert.Equal(CareHomeStatus.Suspended, suspended.Value.Status);
        Assert.Equal(CareHomeStatus.Approved, reactivated.Value.Status);
        Assert.Contains(correction.Value.ReviewHistory, x => x.Reason == "Please replace certificate");
    }

    [Fact]
    public async Task PrivateDocumentFile_IsUnavailableForDraftAndDelegatesReadForReviewableStates()
    {
        using CareHomesDbContext db = CreateDb();
        UserId owner = UserId.New();
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        _ = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        CareHomeFacility draft = await db.Facilities.Include(x => x.Documents)
            .SingleAsync(x => x.Id == new CareHomeId(created.Value.Id));
        CareHomeDocument document = draft.UploadDocument(owner, CareHomeDocumentType.OperatingLicense,
            "private/license.pdf", "application/pdf", 100, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        var storage = new RecordingStorage();
        var handler = new GetAdminCareHomeDocumentFileQueryHandler(db, storage);

        var unavailable = await handler.Handle(new GetAdminCareHomeDocumentFileQuery(draft.Id.Value, document.Id), default);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            if (type != CareHomeDocumentType.OperatingLicense)
                draft.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, DateTime.UtcNow);
        draft.SaveDraft(owner, draft.Version, CareHomeFacilityTests.Draft(), DateTime.UtcNow);
        await db.SaveChangesAsync();
        var submit = new SubmitMyCareHomeApplicationCommandHandler(db);
        await submit.Handle(new SubmitMyCareHomeApplicationCommand(owner, draft.Version), default);
        var readable = await handler.Handle(new GetAdminCareHomeDocumentFileQuery(draft.Id.Value, document.Id), default);

        Assert.Equal("CareHomes.Admin.PrivateDocumentUnavailable", unavailable.Error.Code);
        Assert.True(readable.IsSuccess);
        Assert.Equal("application/pdf", readable.Value.ContentType);
        Assert.Equal(1, storage.Reads);
    }

    private static async Task<CareHomeFacility> CreateSubmittedFacility(CareHomesDbContext db, UserId owner)
    {
        var created = await new CreateCareHomeFacilityCommandHandler(db)
            .Handle(new CreateCareHomeFacilityCommand(owner), default);
        _ = await new SaveMyCareHomeProfileCommandHandler(db).Handle(
            new SaveMyCareHomeProfileCommand(owner, created.Value.Version, CareHomeFacilityTests.Draft()), default);
        CareHomeFacility facility = await db.Facilities.Include(x => x.Revisions).Include(x => x.Documents)
            .SingleAsync(x => x.OwnerUserId == owner);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, DateTime.UtcNow);
        await db.SaveChangesAsync();
        await new SubmitMyCareHomeApplicationCommandHandler(db)
            .Handle(new SubmitMyCareHomeApplicationCommand(owner, facility.Version), default);
        return facility;
    }

    private static CareHomesDbContext CreateDb() => new(new DbContextOptionsBuilder<CareHomesDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class RecordingStorage : IFileStorage
    {
        public int Reads { get; private set; }
        public Task<Result<StoredFile>> SaveAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<StoredFile>> SavePrivateAsync(Stream content, string contentType, long contentLength, string folder, long maxBytes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Result<PrivateFileContent>> OpenReadAsync(string key, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<Result<PrivateFileContent>>(new PrivateFileContent("license.pdf", "application/pdf", new MemoryStream(Encoding.ASCII.GetBytes("private"))));
        }
        public Task<Result> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(Result.Success());
    }
}
