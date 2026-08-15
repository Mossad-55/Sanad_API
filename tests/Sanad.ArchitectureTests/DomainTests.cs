using FluentAssertions;
using NetArchTest.Rules;

namespace Sanad.ArchitectureTests;

public class DomainTests
{
    [Fact]
    public void Domain_Should_Not_Depend_On_MediatR()
    {
        var result = Types.InCurrentDomain()
            .That()
            .ResideInNamespace("Sanad.BuildingBlocks.Domain")
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
