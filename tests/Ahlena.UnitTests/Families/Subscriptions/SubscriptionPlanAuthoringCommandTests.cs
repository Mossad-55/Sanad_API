using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Families.Application.Subscriptions;
using Ahlena.Modules.Families.Domain.Subscriptions;
using Ahlena.Modules.Families.Infrastructure.Persistence;

namespace Ahlena.UnitTests.Families.Subscriptions;

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

    [Fact]
    public async Task Authored_bilingual_copy_and_benefit_labels_are_persisted_on_the_version()
    {
        await using FamiliesDbContext db = CreateContext();
        var command = Command(SubscriptionPlan.Premium, "premium-v2", 2) with
        {
            ArabicName = "الباقة المميزة",
            EnglishName = "Premium plan",
            ArabicDescription = "كل ما تتضمنه الباقة المميزة.",
            EnglishDescription = "Everything the premium plan includes.",
            Benefits = Command(SubscriptionPlan.Premium, "premium-v2", 2).Benefits
                .Select((benefit, index) => index == 0
                    ? benefit with { ArabicLabel = "محادثات", EnglishLabel = "Chatting" }
                    : benefit)
                .ToArray()
        };

        var result = await new CreateSubscriptionPlanVersionCommandHandler(db)
            .Handle(command, default);

        Assert.True(result.IsSuccess);
        SubscriptionPlanVersion stored = await db.SubscriptionPlanVersions.SingleAsync();

        // The copy is version-owned and stored exactly as authored.
        Assert.Equal("الباقة المميزة", stored.ArabicName);
        Assert.Equal("Premium plan", stored.EnglishName);
        Assert.Equal("كل ما تتضمنه الباقة المميزة.", stored.ArabicDescription);
        Assert.Equal("Everything the premium plan includes.", stored.EnglishDescription);

        SubscriptionBenefit labelled = stored.Benefits
            .Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting);
        Assert.Equal("محادثات", labelled.ArabicLabel);
        Assert.Equal("Chatting", labelled.EnglishLabel);

        // The financial terms are untouched by the copy.
        Assert.Equal(SubscriptionPlan.Premium.Price, stored.Price);
        Assert.Equal(SubscriptionPlan.Premium.Cycle, stored.Cycle);
        Assert.Equal(SubscriptionPlan.Premium.Currency, stored.Currency);
        Assert.Equal(SubscriptionPlan.Premium.MemberLimit.Kind, stored.MemberLimitKind);
        Assert.Equal(SubscriptionPlan.Premium.MemberLimit.Value, stored.MemberLimitValue);
        Assert.Equal(SubscriptionPlan.Premium.MonthlyBookingLimit.Kind, stored.MonthlyBookingLimitKind);
        Assert.Equal(SubscriptionPlan.Premium.MonthlyBookingLimit.Value, stored.MonthlyBookingLimitValue);
        Assert.Equal(SubscriptionPlan.Premium.Rollover, stored.Rollover);
    }

    [Fact]
    public async Task A_version_created_without_authored_copy_keeps_the_copy_absent()
    {
        await using FamiliesDbContext db = CreateContext();

        var result = await new CreateSubscriptionPlanVersionCommandHandler(db)
            .Handle(Command(SubscriptionPlan.Free, "legacy", 1), default);

        Assert.True(result.IsSuccess);
        SubscriptionPlanVersion stored = await db.SubscriptionPlanVersions.SingleAsync();

        // Legacy behaviour: the copy was never authored, so it stays absent.
        Assert.Null(stored.ArabicName);
        Assert.Null(stored.EnglishName);
        Assert.Null(stored.ArabicDescription);
        Assert.Null(stored.EnglishDescription);
        Assert.All(stored.Benefits, benefit =>
        {
            Assert.Null(benefit.ArabicLabel);
            Assert.Null(benefit.EnglishLabel);
        });
    }

    [Fact]
    public async Task Two_versions_of_the_same_key_keep_their_own_copy()
    {
        await using FamiliesDbContext db = CreateContext();
        var handler = new CreateSubscriptionPlanVersionCommandHandler(db);

        var first = await handler.Handle(
            Command(
                SubscriptionPlan.Premium,
                "premium",
                1,
                SubscriptionBenefitKey.Chatting,
                "محادثات",
                "Chatting") with
            {
                ArabicName = "النسخة الأولى",
                EnglishName = "First version"
            },
            default);
        var second = await handler.Handle(
            Command(
                SubscriptionPlan.Premium,
                "premium",
                2,
                SubscriptionBenefitKey.Chatting,
                "محادثات موسعة",
                "Extended chatting") with
            {
                ArabicName = "النسخة الثانية",
                EnglishName = "Second version"
            },
            default);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);

        List<SubscriptionPlanVersion> stored = await db.SubscriptionPlanVersions
            .OrderBy(version => version.Version)
            .ToListAsync();
        Assert.Equal(2, stored.Count);
        Assert.Equal("النسخة الأولى", stored[0].ArabicName);
        Assert.Equal("First version", stored[0].EnglishName);
        Assert.Equal("النسخة الثانية", stored[1].ArabicName);
        Assert.Equal("Second version", stored[1].EnglishName);

        // Each version keeps the benefit labels authored for it.
        Assert.Equal(
            "محادثات",
            stored[0].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).ArabicLabel);
        Assert.Equal(
            "Chatting",
            stored[0].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).EnglishLabel);
        Assert.Equal(
            "محادثات موسعة",
            stored[1].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).ArabicLabel);
        Assert.Equal(
            "Extended chatting",
            stored[1].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).EnglishLabel);

        // Publishing one version never touches the other version's copy.
        await new PublishSubscriptionPlanVersionCommandHandler(db)
            .Handle(new PublishSubscriptionPlanVersionCommand(stored[0].Id, UserId.New()), default);
        await db.SaveChangesAsync();

        Assert.Equal("النسخة الأولى", stored[0].ArabicName);
        Assert.Equal("النسخة الثانية", stored[1].ArabicName);
        Assert.Equal(
            "محادثات",
            stored[0].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).ArabicLabel);
        Assert.Equal(
            "Chatting",
            stored[0].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).EnglishLabel);
        Assert.Equal(
            "محادثات موسعة",
            stored[1].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).ArabicLabel);
        Assert.Equal(
            "Extended chatting",
            stored[1].Benefits.Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting).EnglishLabel);
    }

    [Fact]
    public async Task Authored_copy_is_trimmed_and_whitespace_only_copy_becomes_absent()
    {
        await using FamiliesDbContext db = CreateContext();
        var command = Command(
            SubscriptionPlan.Premium,
            "normalized",
            1,
            SubscriptionBenefitKey.Chatting,
            "   ",
            "  Chatting  ") with
        {
            EnglishName = "  Premium  ",
            ArabicName = "   ",
            ArabicDescription = "  كل ما تتضمنه الباقة.  ",
            EnglishDescription = "   "
        };

        var result = await new CreateSubscriptionPlanVersionCommandHandler(db)
            .Handle(command, default);

        Assert.True(result.IsSuccess);
        SubscriptionPlanVersion stored = await db.SubscriptionPlanVersions.SingleAsync();

        // Surrounding whitespace is trimmed, whitespace-only copy is absent.
        Assert.Equal("Premium", stored.EnglishName);
        Assert.Null(stored.ArabicName);
        Assert.Equal("كل ما تتضمنه الباقة.", stored.ArabicDescription);
        Assert.Null(stored.EnglishDescription);

        // The same normalization applies to an authored benefit label.
        SubscriptionBenefit chatting = stored.Benefits
            .Single(benefit => benefit.Key == SubscriptionBenefitKey.Chatting);
        Assert.Null(chatting.ArabicLabel);
        Assert.Equal("Chatting", chatting.EnglishLabel);
    }

    [Fact]
    public async Task Over_long_description_and_benefit_label_are_rejected_and_nothing_is_persisted()
    {
        await using FamiliesDbContext db = CreateContext();
        var handler = new CreateSubscriptionPlanVersionCommandHandler(db);
        var longDescription = Command(SubscriptionPlan.Free, "long-description", 1) with
        {
            EnglishDescription = new string('a', SubscriptionPlanVersion.MaximumDescriptionLength + 1)
        };
        var longLabel = Command(
            SubscriptionPlan.Free,
            "long-label",
            1,
            SubscriptionBenefitKey.Chatting,
            null,
            new string('a', SubscriptionBenefit.MaximumLabelLength + 1));

        var descriptionResult = await handler.Handle(longDescription, default);
        var labelResult = await handler.Handle(longLabel, default);

        Assert.False(descriptionResult.IsSuccess);
        Assert.Equal("Subscriptions.Plan.InvalidTerms", descriptionResult.Error.Code);
        Assert.False(labelResult.IsSuccess);
        Assert.Equal("Subscriptions.Plan.InvalidTerms", labelResult.Error.Code);
        Assert.Empty(db.SubscriptionPlanVersions);
    }

    [Fact]
    public async Task An_over_long_authored_name_is_rejected_and_nothing_is_persisted()
    {
        await using FamiliesDbContext db = CreateContext();
        var command = Command(SubscriptionPlan.Free, "too-long", 1) with
        {
            ArabicName = new string('ا', SubscriptionPlanVersion.MaximumNameLength + 1)
        };

        var result = await new CreateSubscriptionPlanVersionCommandHandler(db)
            .Handle(command, default);

        Assert.False(result.IsSuccess);
        Assert.Equal("Subscriptions.Plan.InvalidTerms", result.Error.Code);
        Assert.Empty(db.SubscriptionPlanVersions);
    }

    private static CreateSubscriptionPlanVersionCommand Command(
        SubscriptionPlan plan,
        string key,
        int version,
        SubscriptionBenefitKey? labelledKey = null,
        string? arabicLabel = null,
        string? englishLabel = null) =>
        new(
            key,
            version,
            plan.Price,
            plan.Cycle,
            plan.Currency,
            plan.Benefits
                .Select(x => x.Key == labelledKey
                    ? new SubscriptionBenefitInput(x.Key, x.IsIncluded, arabicLabel, englishLabel)
                    : new SubscriptionBenefitInput(x.Key, x.IsIncluded))
                .ToArray(),
            new SubscriptionLimitInput(plan.MemberLimit.Kind, plan.MemberLimit.Value),
            new SubscriptionLimitInput(plan.MonthlyBookingLimit.Kind, plan.MonthlyBookingLimit.Value),
            plan.Rollover,
            UserId.New());

    private static FamiliesDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
