namespace Ahlena.UnitTests.Identity.Infrastructure;

[CollectionDefinition(
    "LocalPostgres",
    DisableParallelization = true)]
public sealed class LocalPostgresCollection :
    ICollectionFixture<LocalPostgresIdentityFixture>
{
}