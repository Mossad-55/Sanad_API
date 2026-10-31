using Ahlena.BuildingBlocks.Domain.Enums;
using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Families.Domain.Elderlies;
using Ahlena.Modules.Families.Domain.Families;
using Xunit.Sdk;

namespace Ahlena.UnitTests.Families;

public sealed class ElderlyTests
{
    private static Elderly CreateElderly(
        DateOnly? dateOfBirth = null,
        UserId? identityUserId = null) =>
        Elderly.Create(
            UserId.New(),
            identityUserId ?? UserId.New(),
            new FamilyId(Guid.NewGuid()),
            FamilyRelationshipType.Mother,
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