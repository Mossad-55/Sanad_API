using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionPlanAuthoringCommandTests
{
    [Fact]
    public async Task Super_admin_can_create_a_complete_draft_and_publish_it_once()
    {
        await using FamiliesDbContext db = CreateContext();
        SubscriptionPlan plan = SubscriptionPlan.Premium;
        var create = await new CreateSubscriptionPlanVersionCommandHandler(db).Handle(
            Command(plan, "premium-v2", 2), default);

        Assert.True(create.IsSuccess);
        SubscriptionPlanVersion draft = await db.SubscriptionPlanVersions.SingleAsync();
        Assert.False(draft.IsPublished);
        Assert.Equal(8, draft.Benefits.Count);

        var publish = await new PublishSubscriptionPlanVersionCommandHandler(db).Handle(
            new PublishSubscriptionPlanVersionCommand(draft.Id, UserId.New()), default);
        var repeat = await new PublishSubscriptionPlanVersionCommandHandler(db).Handle(
            new PublishSubscriptionPlanVersionCommand(draft.Id, UserId.New()), default);

        Assert.True(publish.IsSuccess);
        Assert.False(repeat.IsSuccess);
        Assert.Equal("Subscriptions.Plan.AlreadyPublished", repeat.Error.Code);
        Assert.True(draft.IsPublished);
        Assert.NotNull(draft.PublishedOnUtc);
    }

    [Fact]
    public async Task Duplicate_key_and_version_is_rejected()
    {
        await using FamiliesDbContext db = CreateContext();
        var handler = new CreateSubscriptionPlanVersionCommandHandler(db);
        var command = Command(SubscriptionPlan.Free, "managed", 1);

        Assert.True((await handler.Handle(command, default)).IsSuccess);
        var duplicate = await handler.Handle(command, default);

        Assert.False(duplicate.IsSuccess);
        Assert.Equal("Subscriptions.Plan.DuplicateVersion", duplicate.Error.Code);
    }

    [Fact]
    public async Task Invalid_limits_are_rejected_without_persistence()
    {
        await using FamiliesDbContext db = CreateContext();
        var command = Command(SubscriptionPlan.Free, "invalid", 1) with
        {
            MemberLimit = new SubscriptionLimitInput(SubscriptionLimitKind.Finite, null)
        };

        var result = await new CreateSubscriptionPlanVersionCommandHandler(db).Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Plan.InvalidTerms", result.Error.Code);
        Assert.Empty(db.SubscriptionPlanVersions);
    }

    private static CreateSubscriptionPlanVersionCommand Command(
        SubscriptionPlan plan,
        string key,
        int version) =>
        new(
            key,
            version,
            plan.Price,
            plan.Cycle,
            plan.Currency,
            plan.Benefits.Select(x => new SubscriptionBenefitInput(x.Key, x.IsIncluded)).ToArray(),
            new SubscriptionLimitInput(plan.MemberLimit.Kind, plan.MemberLimit.Value),
            new SubscriptionLimitInput(plan.MonthlyBookingLimit.Kind, plan.MonthlyBookingLimit.Value),
            plan.Rollover,
            UserId.New());

    private static FamiliesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
