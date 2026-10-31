using Ahlena.BuildingBlocks.Domain.Exceptions;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Domain.Families;

namespace Ahlena.UnitTests.Families;

public sealed class FamilyTests
{
    [Fact]
    public void Create_WithoutName_DefaultsToMyFamily()
    {
        Family family = Family.Create(UserId.New());

        Assert.Equal("My Family", family.Name);
        Assert.Single(family.Members);
        Assert.Equal(
            FamilyRole.Owner,
            family.Members.Single().Role);
    }

    [Fact]
    public void Rename_RejectsEmpty_AndEnforcesMaxLength()
    {
        Family family = Family.Create(UserId.New());

        Assert.Throws<DomainException>(
            () => family.Rename("   "));

        Assert.Throws<DomainException>(
            () => family.Rename(
                new string('x', Family.MaximumNameLength + 1)));

        family.Rename("The Nasr Family");

        Assert.Equal("The Nasr Family", family.Name);
    }
}