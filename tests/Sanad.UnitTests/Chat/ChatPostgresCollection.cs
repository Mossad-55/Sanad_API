namespace Sanad.UnitTests.Chat;

[CollectionDefinition(
    "ChatPostgresCleanup",
    DisableParallelization = true)]
public sealed class ChatPostgresCollection :
    ICollectionFixture<ChatPostgresCleanupFixture>
{
}
