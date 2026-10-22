using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.MedicalAccess;
using Sanad.Modules.Families.Application.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.Medical;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families;

/// <summary>
/// M5 grant-gated caregiver read of a dependent's structured medical profile.
/// The gate is the persisted <see cref="MedicalAccessGrant"/> row, rechecked on
/// every read: only an ACTIVE grant to the caller carrying <c>CanViewRecords</c>
/// returns the profile; a missing, revoked, expired, foreign-grantee or
/// view-less grant is denied, and an unknown dependent is not found. The
/// response is the exact family read shape.
/// </summary>
public sealed class CaregiverDependentMedicalProfileTests
{
    [Fact]
    public async Task Profile_is_returned_for_an_active_view_grant_to_the_caller()
    {
        var (db, elderly) = SeedDependent();
        UserId grantee = UserId.New();
        AddGrant(db, elderly.Id, grantedBy: UserId.New(), grantee, canView: true, DateTime.UtcNow.AddDays(30));

        var result = await Handler(db, grantee, elderly.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal(elderly.Id, result.Value.DependentId);
        Assert.Equal(BloodType.APositive, result.Value.BloodType);
        Assert.Equal(170, result.Value.HeightCm);
        Assert.Contains("Diabetes", result.Value.ChronicConditions);
    }

    [Fact]
    public async Task Profile_is_denied_when_no_grant_exists()
    {
        var (db, elderly) = SeedDependent();

        var result = await Handler(db, UserId.New(), elderly.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.RecordAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task Profile_is_denied_when_the_grant_does_not_permit_viewing()
    {
        var (db, elderly) = SeedDependent();
        UserId grantee = UserId.New();

        // A grant without CanViewRecords exists; it must not open the profile.
        // The domain forbids edit/share without view, so a view-less grant is
        // seeded directly to model a legacy/foreign row the gate must still
        // honour.
        db.MedicalAccessGrants.Add(MedicalAccessGrant.Create(
            elderly.Id,
            UserId.New(),
            grantee,
            MedicalAccessGrantType.Limited,
            canViewRecords: false,
            canEditRecords: false,
            canShareWithOthers: false,
            DateTime.UtcNow));
        db.SaveChanges();

        var result = await Handler(db, grantee, elderly.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.RecordAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task Profile_is_denied_when_the_grant_was_revoked()
    {
        var (db, elderly) = SeedDependent();
        UserId grantee = UserId.New();
        MedicalAccessGrant grant = AddGrant(
            db, elderly.Id, grantedBy: UserId.New(), grantee, canView: true, DateTime.UtcNow.AddDays(30));
        grant.Revoke(UserId.New(), DateTime.UtcNow);
        db.SaveChanges();

        var result = await Handler(db, grantee, elderly.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.RecordAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task Profile_is_denied_when_the_grant_expired()
    {
        var (db, elderly) = SeedDependent();
        UserId grantee = UserId.New();
        AddGrant(
            db, elderly.Id, grantedBy: UserId.New(), grantee, canView: true,
            expiresOnUtc: DateTime.UtcNow.AddMinutes(-1),
            grantedOnUtc: DateTime.UtcNow.AddDays(-1));
        db.SaveChanges();

        var result = await Handler(db, grantee, elderly.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.RecordAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task Profile_is_denied_for_a_grant_to_a_different_grantee()
    {
        var (db, elderly) = SeedDependent();
        AddGrant(db, elderly.Id, grantedBy: UserId.New(), UserId.New(), canView: true, DateTime.UtcNow.AddDays(30));

        var result = await Handler(db, UserId.New(), elderly.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.RecordAccessDenied", result.Error.Code);
    }

    [Fact]
    public async Task Profile_is_not_found_for_an_unknown_dependent()
    {
        var (db, _) = SeedDependent();

        var result = await Handler(db, UserId.New(), ElderlyId.New());

        Assert.True(result.IsFailure);
        Assert.Equal("MedicalAccess.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Profile_rechecks_the_gate_on_every_read_without_caching()
    {
        var (db, elderly) = SeedDependent();
        UserId grantee = UserId.New();
        MedicalAccessGrant grant = AddGrant(
            db, elderly.Id, grantedBy: UserId.New(), grantee, canView: true, DateTime.UtcNow.AddDays(30));

        Assert.True((await Handler(db, grantee, elderly.Id)).IsSuccess);

        // Revoked between two reads: the second read must immediately deny.
        grant.Revoke(UserId.New(), DateTime.UtcNow);
        db.SaveChanges();

        var afterRevoke = await Handler(db, grantee, elderly.Id);
        Assert.True(afterRevoke.IsFailure);
        Assert.Equal("MedicalAccess.RecordAccessDenied", afterRevoke.Error.Code);
    }

    private static Task<Result<ElderlyMedicalProfileResponse>> Handler(
        FamiliesDbContext db,
        UserId grantee,
        ElderlyId dependentId) =>
        new GetCaregiverDependentMedicalProfileQueryHandler(db)
            .Handle(new GetCaregiverDependentMedicalProfileQuery(grantee, dependentId), CancellationToken.None);

    private static MedicalAccessGrant AddGrant(
        FamiliesDbContext db,
        ElderlyId dependentId,
        UserId grantedBy,
        UserId grantee,
        bool canView,
        DateTime? expiresOnUtc,
        DateTime? grantedOnUtc = null)
    {
        MedicalAccessGrant grant = MedicalAccessGrant.Create(
            dependentId,
            grantedBy,
            grantee,
            MedicalAccessGrantType.Limited,
            canViewRecords: canView,
            canEditRecords: false,
            canShareWithOthers: false,
            grantedOnUtc ?? DateTime.UtcNow,
            expiresOnUtc);
        db.MedicalAccessGrants.Add(grant);
        db.SaveChanges();
        return grant;
    }

    private static FamiliesDbContext CreateDbContext() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static (FamiliesDbContext DbContext, Elderly Elderly) SeedDependent()
    {
        var db = CreateDbContext();
        var family = Family.Create(UserId.New(), "Medical Profile Family");
        var elderly = Elderly.Create(
            family.OwnerUserId,
            UserId.New(),
            family.Id,
            FamilyRelationshipType.Grandfather,
            FullName.Create("مسن تجريبي"),
            FullName.Create("Elderly Test"),
            Gender.Male,
            DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-70)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        elderly.UpdateMedicalProfile(ElderlyMedicalProfile.Create(
            BloodType.APositive,
            170,
            78.5m,
            ["Diabetes"],
            [],
            []));

        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();

        return (db, elderly);
    }
}
