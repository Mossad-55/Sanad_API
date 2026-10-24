using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.CareRecords;

namespace Sanad.UnitTests.Families;

/// <summary>
/// W1 care-record aggregate invariants: required plain-text fields, locked
/// source/attribution immutability, the version-based concurrency counter and
/// the append-only payload-free audit rules.
/// </summary>
public sealed class CareRecordDomainTests
{
    private static readonly DateTime UtcNow = new(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FamilyCreate_BindsExactlyFamilyAttribution()
    {
        var elderly = ElderlyId.New();
        var author = UserId.New();
        var family = FamilyId.New();

        var record = CareRecord.CreateForFamily(
            elderly, author, family, "القصر العيني", new DateOnly(2026, 10, 1), "متابعة ضغط الدم", UtcNow);

        Assert.Equal(CareRecordSource.Family, record.Source);
        Assert.Equal(elderly, record.ElderlyId);
        Assert.Equal(author, record.AuthorUserId);
        Assert.Equal(family, record.AuthorFamilyId);
        Assert.Null(record.AuthorFacilityId);
        Assert.Null(record.AuthorCaregiverId);
        Assert.Equal(1, record.Version);
        Assert.Null(record.UpdatedOnUtc);
        Assert.Equal(new DateOnly(2026, 10, 1), record.VisitDate);
    }

    [Fact]
    public void CaregiverCreate_BindsExactlyCaregiverAttribution()
    {
        var record = CareRecord.CreateForCaregiver(
            ElderlyId.New(), UserId.New(), CaregiverId.New(), "As-Salam Hospital",
            new DateOnly(2026, 10, 2), "Wound dressing changed", UtcNow);

        Assert.Equal(CareRecordSource.Caregiver, record.Source);
        Assert.NotNull(record.AuthorCaregiverId);
        Assert.Null(record.AuthorFamilyId);
        Assert.Null(record.AuthorFacilityId);
    }

    [Fact]
    public void CareHomeCreate_BindsExactlyFacilityAttribution()
    {
        var record = CareRecord.CreateForCareHome(
            ElderlyId.New(), UserId.New(), Guid.NewGuid(), "الرحمة لرعاية المسنين",
            new DateOnly(2026, 10, 3), "تفاقم ضعف التنفس", UtcNow);

        Assert.Equal(CareRecordSource.CareHome, record.Source);
        Assert.NotNull(record.AuthorFacilityId);
        Assert.Null(record.AuthorFamilyId);
        Assert.Null(record.AuthorCaregiverId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsWhitespaceHospitalName(string hospitalName)
    {
        Assert.Throws<DomainException>(() => CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(),
            hospitalName, new DateOnly(2026, 10, 1), "تفاصيل", UtcNow));
    }

    [Fact]
    public void Create_RejectsHospitalNameOver200()
    {
        Assert.Throws<DomainException>(() => CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(),
            new string('م', 201), new DateOnly(2026, 10, 1), "تفاصيل", UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsWhitespaceDetails(string details)
    {
        Assert.Throws<DomainException>(() => CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(), "مستشفى", new DateOnly(2026, 10, 1), details, UtcNow));
    }

    [Fact]
    public void Create_RejectsDetailsOver4000()
    {
        Assert.Throws<DomainException>(() => CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(),
            "مستشفى", new DateOnly(2026, 10, 1), new string('ت', 4001), UtcNow));
    }

    [Fact]
    public void Create_RejectsControlCharactersInDetails()
    {
        Assert.Throws<DomainException>(() => CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(),
            "مستشفى", new DateOnly(2026, 10, 1), "تفاصيل\u0000خفية", UtcNow));
    }

    [Fact]
    public void Create_TrimsRequiredFields()
    {
        var record = CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(),
            "  مستشفى النيل  ", new DateOnly(2026, 10, 1), "  تفاصيل الزيارة  ", UtcNow);

        Assert.Equal("مستشفى النيل", record.HospitalName);
        Assert.Equal("تفاصيل الزيارة", record.Details);
    }

    [Fact]
    public void Create_RejectsNonUtcTimestamps()
    {
        Assert.Throws<DomainException>(() => CareRecord.CreateForFamily(
            ElderlyId.New(), UserId.New(), FamilyId.New(),
            "مستشفى", new DateOnly(2026, 10, 1),
            "تفاصيل", DateTime.SpecifyKind(UtcNow, DateTimeKind.Local)));
    }

    [Fact]
    public void Update_ChangesContentOnly_AndBumpsVersion()
    {
        var record = CareRecord.CreateForCaregiver(
            ElderlyId.New(), UserId.New(), CaregiverId.New(), "Old Hospital",
            new DateOnly(2026, 10, 1), "old details", UtcNow);

        record.Update("New Hospital", new DateOnly(2026, 10, 2), "new details", UtcNow.AddHours(1));

        Assert.Equal("New Hospital", record.HospitalName);
        Assert.Equal(new DateOnly(2026, 10, 2), record.VisitDate);
        Assert.Equal("new details", record.Details);
        Assert.Equal(2, record.Version);
        Assert.Equal(UtcNow.AddHours(1), record.UpdatedOnUtc);
    }

    [Fact]
    public void Update_RejectsWhitespaceHospitalName()
    {
        var record = CareRecord.CreateForCaregiver(
            ElderlyId.New(), UserId.New(), CaregiverId.New(), "Old Hospital",
            new DateOnly(2026, 10, 1), "old details", UtcNow);

        Assert.Throws<DomainException>(() => record.Update("  ", new DateOnly(2026, 10, 2), "x", UtcNow));
    }

    [Fact]
    public void Audits_AdminReadMustUseAdminReadFactory()
    {
        Assert.Throws<DomainException>(() => CareRecordAudit.RecordAuthored(
            Guid.Empty, ElderlyId.New(), CareRecordSource.Family, UserId.New(),
            CareRecordAuditAction.Created, "trace", UtcNow));

        // Authoring rows must carry the record id and its source, so a
        // Deleted row still states which source produced it.
        var audit = CareRecordAudit.RecordAuthored(
            Guid.NewGuid(), ElderlyId.New(), CareRecordSource.Caregiver, UserId.New(),
            CareRecordAuditAction.Deleted, "trace", UtcNow);
        Assert.Equal(CareRecordSource.Caregiver, audit.Source);
        Assert.NotNull(audit.RecordId);

        // Admin list rows are the payload-free read audit: no single record.
        var listAudit = CareRecordAudit.RecordAdminRead(
            recordId: null, elderlyId: null, UserId.New(),
            CareRecordAuditAction.AdminListed, "trace", UtcNow);
        Assert.Null(listAudit.RecordId);
        Assert.Null(listAudit.Source);
    }

    [Fact]
    public void Audits_DetailReadRequiresRecordId()
    {
        Assert.Throws<DomainException>(() => CareRecordAudit.RecordAdminRead(
            recordId: null, elderlyId: null, UserId.New(),
            CareRecordAuditAction.AdminDetailRead, "trace", UtcNow));

        var detailAudit = CareRecordAudit.RecordAdminRead(
            Guid.NewGuid(), ElderlyId.New(), UserId.New(),
            CareRecordAuditAction.AdminDetailRead, "trace", UtcNow);
        Assert.NotNull(detailAudit.RecordId);
        // Payload-free: an admin read audit row never invents a source.
        Assert.Null(detailAudit.Source);
    }

    [Fact]
    public void Audits_RejectBadCorrelationOrTime()
    {
        Assert.Throws<DomainException>(() => CareRecordAudit.RecordAdminRead(
            Guid.NewGuid(), ElderlyId.New(), UserId.New(),
            CareRecordAuditAction.AdminDetailRead, "  ", UtcNow));

        Assert.Throws<DomainException>(() => CareRecordAudit.RecordAdminRead(
            Guid.NewGuid(), ElderlyId.New(), UserId.New(),
            CareRecordAuditAction.AdminDetailRead, new string('x', 201), UtcNow));

        Assert.Throws<DomainException>(() => CareRecordAudit.RecordAdminRead(
            Guid.NewGuid(), ElderlyId.New(), UserId.New(),
            CareRecordAuditAction.AdminDetailRead, "trace",
            DateTime.SpecifyKind(UtcNow, DateTimeKind.Unspecified)));
    }
}
