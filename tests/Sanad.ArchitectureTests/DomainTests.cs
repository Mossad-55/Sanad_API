using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Sanad.BuildingBlocks.Domain.Abstractions;
using Sanad.Modules.Caregivers.Domain.Caregivers;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Identity.Domain.Users;
using Sanad.Modules.Cms.Domain.Splash;

namespace Sanad.ArchitectureTests;

public sealed class DomainTests
{
    private static readonly Assembly[] DomainAssemblies =
    [
        typeof(AggregateRoot<>).Assembly,
        typeof(User).Assembly,
        typeof(Family).Assembly,
        typeof(Caregiver).Assembly,
        typeof(SplashScreen).Assembly
    ];

    [Fact]
    public void DomainAssemblies_ShouldNotDependOnMediatR()
    {
        TestResult result = Types
            .InAssemblies(DomainAssemblies)
            .ShouldNot()
            .HaveDependencyOn("MediatR")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain assemblies must remain independent of MediatR");
    }
}