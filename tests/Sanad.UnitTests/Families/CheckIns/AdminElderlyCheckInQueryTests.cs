using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.CheckIns;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.CheckIns;

public sealed class AdminElderlyCheckInQueryTests
{
    [Fact]
    public async Task List_ProjectsSafeOperationalFields_FiltersAndNormalizesPaging_AndAuditsFirst()
    {
        await using var db = CreateDb(out var actor, out var elderly, out var first, out var second);
        var result = await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "list", 0, 0, elderly.Id.Value, first.LocalDate, second.LocalDate, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(second.Id, item.Id);
        Assert.Equal("Elderly", item.ElderlyEnglishName);
        Assert.Equal(elderly.FamilyId.Value, item.FamilyId);
        Assert.Equal(false, item.Answer);
        Assert.Equal(1, result.Value.Page);
        Assert.Equal(20, result.Value.PageSize);
        Assert.Equal(1, result.Value.TotalCount);
        var audit = Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
        Assert.Equal("ListCheckIns", audit.Action);
        Assert.Equal("list", audit.CorrelationId);
    }

    [Fact]
    public async Task Detail_MissingResourceReturnsNotFoundAfterAudit()
    {
        await using var db = CreateDb(out var actor, out _, out _, out _);
        var missing = Guid.NewGuid();

        var result = await new GetAdminElderlyCheckInQueryHandler(db).Handle(
            new(actor, "SuperAdmin", "detail", missing), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.AdminCheckIn.NotFound", result.Error.Code);
        var audit = Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync());
        Assert.Equal("GetCheckIn", audit.Action);
        Assert.Equal(missing, audit.ResourceId);
    }

    [Fact]
    public async Task Timeline_IsInclusive_AndRejectsReversedOrOver31DayRanges()
    {
        await using var db = CreateDb(out var actor, out var elderly, out var first, out var second);
        var inclusive = await new GetAdminElderlyCheckInTimelineQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "timeline", elderly.Id.Value, first.LocalDate, second.LocalDate), CancellationToken.None);
        Assert.True(inclusive.IsSuccess);
        Assert.Equal([first.LocalDate, second.LocalDate], inclusive.Value.Select(x => x.LocalDate));

        var reversed = await new GetAdminElderlyCheckInTimelineQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "reversed", elderly.Id.Value, second.LocalDate, first.LocalDate), CancellationToken.None);
        var tooWide = await new GetAdminElderlyCheckInTimelineQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "wide", elderly.Id.Value, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)), CancellationToken.None);
        Assert.Equal("Families.AdminCheckIn.InvalidDateRange", reversed.Error.Code);
        Assert.Equal("Families.AdminCheckIn.InvalidDateRange", tooWide.Error.Code);
        Assert.Equal(1, await db.AdminMedicationAccessAudits.CountAsync());
    }

    [Fact]
    public async Task Aggregate_FiltersByDateAndElderly_AndAuditsBeforeRead()
    {
        await using var db = CreateDb(out var actor, out var elderly, out var first, out var second);
        var result = await new GetAdminElderlyCheckInAggregateQueryHandler(db).Handle(
            new(actor, "SuperAdmin", "aggregate", elderly.Id.Value, first.LocalDate, second.LocalDate), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Total);
        Assert.Equal(1, result.Value.Positive);
        Assert.Equal(1, result.Value.Negative);
        Assert.Equal("GetCheckInAggregate", Assert.Single(await db.AdminMedicationAccessAudits.ToListAsync()).Action);
    }

    [Fact]
    public async Task ListAndDetail_ExcludeDeletedFamilyRows_WithoutSensitiveProfileFields()
    {
        await using var db = CreateDb(out var actor, out var elderly, out var first, out _);
        var family = await db.Families.SingleAsync(x => x.Id == elderly.FamilyId);
        family.MarkDeleted("request", null);
        await db.SaveChangesAsync();

        var list = await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "deleted-list"), CancellationToken.None);
        var detail = await new GetAdminElderlyCheckInQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "deleted-detail", first.Id), CancellationToken.None);

        Assert.Empty(list.Value.Items);
        Assert.True(detail.IsFailure);
        Assert.DoesNotContain("Phone", string.Join(',', typeof(AdminElderlyCheckInRecord).GetProperties().Select(x => x.Name)));
        Assert.DoesNotContain("Address", string.Join(',', typeof(AdminElderlyCheckInRecord).GetProperties().Select(x => x.Name)));
        Assert.DoesNotContain("Clinical", string.Join(',', typeof(AdminElderlyCheckInRecord).GetProperties().Select(x => x.Name)));
        Assert.DoesNotContain("TimeZoneId", string.Join(',', typeof(AdminElderlyCheckInRecord).GetProperties().Select(x => x.Name)));
    }

    [Fact]
    public async Task Aggregate_ExcludesDeletedFamilyRows()
    {
        await using var db = CreateDb(out var actor, out var elderly, out _, out _);
        var family = await db.Families.SingleAsync(x => x.Id == elderly.FamilyId);
        family.MarkDeleted("request", null);
        await db.SaveChangesAsync();

        var result = await new GetAdminElderlyCheckInAggregateQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "deleted-aggregate"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.Total);
    }

    [Fact]
    public async Task ListAndAggregate_RejectReversedAndOver31DayRanges()
    {
        await using var db = CreateDb(out var actor, out var elderly, out var first, out var second);
        var list = await new GetAdminElderlyCheckInsQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "bad-list", StartDate: second.LocalDate, EndDate: first.LocalDate), CancellationToken.None);
        var aggregate = await new GetAdminElderlyCheckInAggregateQueryHandler(db).Handle(
            new(actor, "SupportAdmin", "bad-aggregate", elderly.Id.Value, new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)), CancellationToken.None);
        Assert.Equal("Families.AdminCheckIn.InvalidDateRange", list.Error.Code);
        Assert.Equal("Families.AdminCheckIn.InvalidDateRange", aggregate.Error.Code);
    }

    private static FamiliesDbContext CreateDb(out UserId actor, out Elderly elderly, out ElderlyCheckIn first, out ElderlyCheckIn second)
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        actor = UserId.New();
        var owner = UserId.New();
        var family = Family.Create(owner);
        elderly = Elderly.Create(owner, UserId.New(), family.Id, FamilyRelationshipType.Father,
            FullName.Create("الاسم"), FullName.Create("Elderly"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2026, 1, 1));
        first = ElderlyCheckIn.Create(elderly.Id, new DateOnly(2026, 9, 1), true,
            new TimeOnly(8, 0), new DateTime(2026, 9, 1, 6, 0, 0, DateTimeKind.Utc), actor);
        second = ElderlyCheckIn.Create(elderly.Id, new DateOnly(2026, 9, 2), false,
            new TimeOnly(8, 0), new DateTime(2026, 9, 2, 6, 0, 0, DateTimeKind.Utc), actor);
        db.AddRange(family, elderly, first, second);
        db.SaveChanges();
        return db;
    }
}
