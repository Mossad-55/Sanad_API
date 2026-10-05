using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.UnitTests.Families;

public sealed class MedicalAccessGrantTests
{
    [Fact]
    public void Create_RejectsEditPermissionWithoutViewPermission()
    {
        Assert.Throws<DomainException>(() => MedicalAccessGrant.Create(
            ElderlyId.New(), UserId.New(), UserId.New(), MedicalAccessGrantType.Limited,
            canViewRecords: false, canEditRecords: true, canShareWithOthers: false,
            DateTime.UtcNow));
    }

    [Fact]
    public void Create_RejectsExpiryBeforeCreation()
    {
        var created = DateTime.UtcNow;
        Assert.Throws<DomainException>(() => MedicalAccessGrant.Create(
            ElderlyId.New(), UserId.New(), UserId.New(), MedicalAccessGrantType.Full,
            true, false, false, created, created.AddTicks(-1)));
    }

    [Fact]
    public void Revoke_RecordsActorAndCannotBeRepeated()
    {
        var grant = MedicalAccessGrant.Create(
            ElderlyId.New(), UserId.New(), UserId.New(), MedicalAccessGrantType.Emergency,
            true, false, false, DateTime.UtcNow);
        var actor = UserId.New();

        grant.Revoke(actor, DateTime.UtcNow.AddSeconds(1));

        Assert.Equal(actor, grant.RevokedByUserId);
        Assert.NotNull(grant.RevokedOnUtc);
        Assert.Throws<DomainException>(() => grant.Revoke(actor, DateTime.UtcNow.AddSeconds(2)));
    }
}
