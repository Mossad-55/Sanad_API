using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Domain.Elderlies;

namespace Sanad.UnitTests.Families;

public sealed class ElderlyTests
{
    private static Elderly CreateElderly(
        DateOnly? dateOfBirth = null,
        UserId? identityUserId = null) =>
        Elderly.Create(
            UserId.New(),
            identityUserId ?? UserId.New(),
            new FamilyId(Guid.NewGuid()),
            FullName.Create("فاطمة"),
            FullName.Create("Fatima"),
            Gender.Female,
            dateOfBirth ?? new DateOnly(1945, 3, 10),
            DateOnly.FromDateTime(DateTime.UtcNow));

    [Fact]
    public void Create_RejectsEmptyIdentityUserId()
    {
        Assert.Throws<DomainException>(
            () => CreateElderly(identityUserId: UserId.Empty));
    }

    [Fact]
    public void Create_RejectsFutureDateOfBirth()
    {
        DateOnly future =
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1);

        Assert.Throws<DomainException>(
            () => CreateElderly(dateOfBirth: future));
    }
}