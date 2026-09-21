using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Families.Application.Subscriptions;
using Sanad.Modules.Families.Domain.Subscriptions;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Subscriptions;

public sealed class SubscriptionCouponTests
{
    [Fact]
    public void Create_normalizes_code_and_rounds_percentage()
    {
        var created = DateTime.UtcNow;
        var coupon = SubscriptionCoupon.Create("  premium10 ", Guid.NewGuid(), 12.346m, created.AddDays(1), created);

        Assert.Equal("PREMIUM10", coupon.Code);
        Assert.Equal(12.35m, coupon.DiscountPercentage);
        Assert.Equal(DateTimeKind.Utc, coupon.ExpiresOnUtc.Kind);
    }

    [Theory]
    [InlineData("AB", 10)]
    [InlineData("VALID", 0)]
    [InlineData("VALID", 100.01)]
    public void Create_rejects_invalid_terms(string code, decimal percentage)
    {
        var exception = Assert.Throws<Sanad.BuildingBlocks.Domain.Exceptions.DomainException>(() =>
            SubscriptionCoupon.Create(code, Guid.NewGuid(), percentage, DateTime.UtcNow.AddDays(1)));

        Assert.NotEmpty(exception.Message);
    }

    [Fact]
    public async Task Create_handler_requires_published_available_plan_and_rejects_duplicate_code()
    {
        await using var db = CreateContext();
        var published = PublishedPlan();
        var draft = SubscriptionPlanVersion.Create(SubscriptionPlan.Free);
        db.SubscriptionPlanVersions.AddRange(published, draft);
        await db.SaveChangesAsync();
        var handler = new CreateSubscriptionCouponCommandHandler(db);
        var expiry = DateTime.UtcNow.AddDays(7);

        var created = await handler.Handle(new(" welcome ", published.Id, 10m, expiry, UserId.New()), default);
        var duplicate = await handler.Handle(new("WELCOME", published.Id, 10m, expiry, UserId.New()), default);
        var unavailable = await handler.Handle(new("DRAFT", draft.Id, 10m, expiry, UserId.New()), default);

        Assert.True(created.IsSuccess);
        Assert.Equal("Subscriptions.Coupon.DuplicateCode", duplicate.Error.Code);
        Assert.Equal("Subscriptions.Coupon.PlanNotFound", unavailable.Error.Code);
        Assert.Equal("WELCOME", (await db.SubscriptionCoupons.SingleAsync()).Code);
    }

    [Fact]
    public async Task Queries_list_and_delete_coupon()
    {
        await using var db = CreateContext();
        var plan = PublishedPlan();
        db.SubscriptionPlanVersions.Add(plan);
        await db.SaveChangesAsync();
        var created = await new CreateSubscriptionCouponCommandHandler(db).Handle(
            new("SAVE20", plan.Id, 20m, DateTime.UtcNow.AddDays(2), UserId.New()), default);

        var list = await new GetSubscriptionCouponsQueryHandler(db).Handle(new(), default);
        var detail = await new GetSubscriptionCouponQueryHandler(db).Handle(new(created.Value), default);
        var deleted = await new DeleteSubscriptionCouponCommandHandler(db).Handle(
            new(created.Value, UserId.New()), default);

        Assert.True(list.IsSuccess);
        Assert.Equal("SAVE20", Assert.Single(list.Value).Code);
        Assert.Equal(created.Value, detail.Value.Id);
        Assert.True(deleted.IsSuccess);
        Assert.Empty(db.SubscriptionCoupons);
    }

    private static SubscriptionPlanVersion PublishedPlan() =>
        SubscriptionPlanVersion.Create(
            SubscriptionPlan.Premium,
            isPublished: true,
            isAvailableForNewSales: true,
            createdOnUtc: DateTime.UtcNow,
            publishedOnUtc: DateTime.UtcNow);

    private static FamiliesDbContext CreateContext() => new(
        new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
