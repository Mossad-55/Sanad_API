using Microsoft.EntityFrameworkCore;
using Sanad.Modules.Identity.Application.Feedback;
using Sanad.UnitTests.Identity.Registration;

namespace Sanad.UnitTests.Identity.Feedback;

public sealed class AppRatingFeedbackCommandHandlerTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task Handle_InvalidRating_ReturnsValidationFailureWithoutWriting(int rating)
    {
        await using var db = CreateDb();
        var handler = new AppRatingFeedbackCommandHandler(db);

        var result = await handler.Handle(
            new AppRatingFeedbackCommand(Guid.NewGuid(), rating, null, null, null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal("Feedback.InvalidRating", result.Error.Code);
        Assert.Equal(0, await db.Feedbacks.CountAsync());
        Assert.Equal(0, db.SaveChangesCalls);
    }

    [Fact]
    public async Task Handle_ValidRating_PersistsAuthenticatedUserAndMetadata()
    {
        await using var db = CreateDb();
        var userId = Guid.NewGuid();
        var handler = new AppRatingFeedbackCommandHandler(db);

        var result = await handler.Handle(
            new AppRatingFeedbackCommand(userId, 5, "Very helpful", "ios", "1.2.3"),
            default);

        Assert.True(result.IsSuccess);
        var feedback = await db.Feedbacks.SingleAsync();
        Assert.Equal(userId, feedback.UserId.Value);
        Assert.Equal(5, feedback.Rating);
        Assert.Equal("Very helpful", feedback.Comment);
        Assert.Equal("ios", feedback.DeviceInfo);
        Assert.Equal("1.2.3", feedback.AppVersion);
        Assert.Equal(1, db.SaveChangesCalls);
    }

    private static IdentityTestDbContext CreateDb() => new(
        new DbContextOptionsBuilder<IdentityTestDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
