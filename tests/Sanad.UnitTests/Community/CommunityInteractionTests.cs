using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Community.Domain.Interactions;

namespace Sanad.UnitTests.Community;

public sealed class CommunityInteractionTests
{
    [Fact]
    public void Create_RejectsFavoriteOnComment()
    {
        Assert.Throws<ArgumentException>(() => CommunityInteraction.Create(
            CommunityInteractionTarget.Comment,
            Guid.NewGuid(),
            UserId.New(),
            CommunityInteractionKind.Favorite,
            DateTime.UtcNow));
    }

    [Fact]
    public void Create_RequiresActor()
    {
        Assert.Throws<ArgumentException>(() => CommunityInteraction.Create(
            CommunityInteractionTarget.Post,
            Guid.NewGuid(),
            UserId.Empty,
            CommunityInteractionKind.Like,
            DateTime.UtcNow));
    }
}
