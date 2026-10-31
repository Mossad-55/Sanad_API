using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using Ahlena.BuildingBlocks.Domain.Abstractions;
using Ahlena.Modules.Caregivers.Domain.Caregivers;
using Ahlena.Modules.Families.Domain.Families;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Cms.Domain.Splash;

namespace Ahlena.ArchitectureTests;

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