namespace Ahlena.UnitTests.Chat;

[CollectionDefinition(
    "ChatPostgresCleanup",
    DisableParallelization = true)]
public sealed class ChatPostgresCollection :
    ICollectionFixture<ChatPostgresCleanupFixture>
{
}
