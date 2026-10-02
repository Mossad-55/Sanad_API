using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.CareHomes.Domain.Facilities;

namespace Sanad.UnitTests.CareHomes;

public sealed class CareHomeFacilityTests
{
    [Fact]
    public void Draft_AllowsIncompleteProfile_ButSubmitRequiresBilingualContentAndDocuments()
    {
        DateTime now = UtcNow();
        UserId owner = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        facility.SaveDraft(owner, facility.Version, Draft(arabicName: "", englishDescription: ""), now);

        Assert.Throws<DomainException>(() => facility.Submit(owner, facility.Version, now));
    }

    [Fact]
    public void Submit_FreezesRevision_AndAdminMustExplicitlyVerifyNonExpiringDocuments()
    {
        DateTime now = UtcNow();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        CareHomeProfileRevision revision = facility.SaveDraft(owner, facility.Version, Draft(), now);
        List<CareHomeDocument> documents = [];
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            documents.Add(facility.UploadDocument(owner, type, $"private/{type}.pdf", "application/pdf", 100, null, now));
        facility.Submit(owner, facility.Version, now);

        Assert.True(revision.IsFrozen);
        Assert.Throws<DomainException>(() => facility.SaveDraft(owner, facility.Version, Draft(), now.AddMinutes(1)));
        Assert.Throws<DomainException>(() => facility.VerifyDocument(admin, facility.Version, documents[0].Id, null, false, now.AddMinutes(1)));

        foreach (CareHomeDocument document in documents)
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, now.AddMinutes(2));
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, now.AddMinutes(3), DateOnly.FromDateTime(now));

        Assert.Equal(CareHomeStatus.Approved, facility.Status);
        Assert.Equal(revision.Id, facility.ApprovedRevisionId);
    }

    [Fact]
    public void Submit_RejectsStaleVersion_AndOwnerCannotChangeAnotherFacility()
    {
        DateTime now = UtcNow();
        UserId owner = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);

        Assert.Throws<DomainException>(() => facility.SaveDraft(UserId.New(), facility.Version, Draft(), now));
        Assert.Throws<DomainException>(() => facility.SaveDraft(owner, facility.Version + 1, Draft(), now));
        Assert.Throws<DomainException>(() => facility.Submit(owner, facility.Version + 1, now));
        Assert.Throws<DomainException>(() => facility.VerifyDocument(UserId.New(), facility.Version + 1, Guid.NewGuid(), null, false, now));
    }

    [Fact]
    public void ApprovedProfile_RemainsApprovedAndReferencedWhileSensitiveRevisionIsReviewed()
    {
        DateTime now = UtcNow();
        UserId owner = UserId.New();
        UserId admin = UserId.New();
        CareHomeFacility facility = CareHomeFacility.CreateDraft(owner, now);
        CareHomeProfileRevision initial = facility.SaveDraft(owner, facility.Version, Draft(), now);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/initial-{type}.pdf", "application/pdf", 100, null, now);
        facility.Submit(owner, facility.Version, now);
        foreach (CareHomeDocument document in facility.Documents.ToArray())
            facility.VerifyDocument(admin, facility.Version, document.Id, null, true, now);
        facility.Review(admin, facility.Version, CareHomeReviewAction.Approved, null, now, DateOnly.FromDateTime(now));

        CareHomeProfileDraft changed = Draft() with { Address = "Updated address" };
        CareHomeProfileRevision pendingRevision = facility.SaveDraft(owner, facility.Version, changed, now.AddMinutes(1));
        Assert.Equal(CareHomeStatus.Approved, facility.Status);
        Assert.Equal(initial.Id, facility.ApprovedRevisionId);
        foreach (CareHomeDocumentType type in Enum.GetValues<CareHomeDocumentType>())
            facility.UploadDocument(owner, type, $"private/revision-{type}.pdf", "application/pdf", 100, null, now.AddMinutes(2));
        facility.Submit(owner, facility.Version, now.AddMinutes(3));

        Assert.Equal(CareHomeStatus.PendingReview, facility.Status);
        Assert.Equal(initial.Id, facility.ApprovedRevisionId);
        Assert.Equal(pendingRevision.Id, facility.SubmittedRevisionId);
    }

    internal static CareHomeProfileDraft Draft(
        string arabicName = "دار الأمل",
        string englishDescription = "Care and recovery") => new(
        arabicName,
        "Hope House",
        "رعاية وإقامة",
        englishDescription,
        "Contact",
        "+201000000000",
        null,
        "Cairo",
        "Nasr City",
        "District 1",
        "Example address",
        "الشروط",
        "Admission conditions",
        [],
        []);

    private static DateTime UtcNow() => DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc);
}
