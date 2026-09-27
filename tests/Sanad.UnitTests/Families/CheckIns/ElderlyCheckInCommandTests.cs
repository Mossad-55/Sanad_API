using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.Notifications;
using Sanad.Modules.Families.Application.CheckIns;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Elderlies.CheckIns;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.CheckIns;

public sealed class ElderlyCheckInCommandTests
{
    [Fact]
    public async Task NegativeAnswer_IsPersistedOnce_AndSameAnswerRetryReattemptsAlertDelivery()
    {
        await using var db = CreateDb(out var elderly, out var actor);
        var alerts = new RecordingAlerts();
        var handler = new SubmitElderlyCheckInCommandHandler(db, alerts);

        var first = await handler.Handle(new(actor, false), CancellationToken.None);
        var retry = await handler.Handle(new(actor, false), CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(first.Value.Id, retry.Value.Id);
        Assert.Equal(first.Value.LocalDate, retry.Value.LocalDate);
        Assert.Equal("Africa/Cairo", first.Value.TimeZoneId);
        Assert.Single(await db.ElderlyCheckIns.ToListAsync());
        Assert.Equal(2, alerts.Requests.Count);
    }

    [Fact]
    public async Task ConflictingSameDayAnswer_IsRejectedWithoutMutationOrAlert()
    {
        await using var db = CreateDb(out var elderly, out var actor);
        var alerts = new RecordingAlerts();
        var handler = new SubmitElderlyCheckInCommandHandler(db, alerts);

        Assert.True((await handler.Handle(new(actor, true), CancellationToken.None)).IsSuccess);
        var conflicting = await handler.Handle(new(actor, false), CancellationToken.None);

        Assert.True(conflicting.IsFailure);
        Assert.Equal("Families.ElderlyCheckIn.AlreadyAnswered", conflicting.Error.Code);
        Assert.Single(await db.ElderlyCheckIns.ToListAsync());
        Assert.Empty(alerts.Requests);
    }

    [Fact]
    public async Task DeletedFamily_IsIsolatedFromSubmission()
    {
        await using var db = CreateDb(out var elderly, out var actor);
        var family = await db.Families.SingleAsync();
        family.MarkDeleted("owner request", null);
        await db.SaveChangesAsync();
        var alerts = new RecordingAlerts();

        var result = await new SubmitElderlyCheckInCommandHandler(db, alerts)
            .Handle(new(actor, false), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.ElderlyCheckIn.NotFound", result.Error.Code);
        Assert.Empty(await db.ElderlyCheckIns.ToListAsync());
        Assert.Empty(alerts.Requests);
    }

    [Fact]
    public async Task InvalidPersistedTimezone_FailsClosedBeforeCreatingCheckIn()
    {
        await using var db = CreateDb(out var elderly, out var actor);
        typeof(Elderly).GetProperty(nameof(Elderly.TimeZoneId))!.SetValue(elderly, "Mars/Olympus");
        await db.SaveChangesAsync();

        var result = await new SubmitElderlyCheckInCommandHandler(db, new RecordingAlerts())
            .Handle(new(actor, true), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.ElderlyCheckIn.InvalidTimeZone", result.Error.Code);
        Assert.Empty(await db.ElderlyCheckIns.ToListAsync());
    }

    [Fact]
    public void CheckInPersistence_UsesElderlyAndLocalDateAsUniqueBoundary()
    {
        using var db = CreateDb(out _, out _);
        var index = db.Model.FindEntityType(typeof(ElderlyCheckIn))!.GetIndexes()
            .Single(x => x.Properties.Select(p => p.Name).SequenceEqual([
                nameof(ElderlyCheckIn.ElderlyId), nameof(ElderlyCheckIn.LocalDate)]));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void IanaTimezoneConversion_PreservesLocalDayAcrossDstTransitions()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var beforeSpringTransition = TimeZoneInfo.ConvertTimeFromUtc(
            new DateTime(2026, 3, 8, 6, 59, 0, DateTimeKind.Utc), zone);
        var afterSpringTransition = TimeZoneInfo.ConvertTimeFromUtc(
            new DateTime(2026, 3, 8, 7, 1, 0, DateTimeKind.Utc), zone);
        var beforeFallTransition = TimeZoneInfo.ConvertTimeFromUtc(
            new DateTime(2026, 11, 1, 5, 59, 0, DateTimeKind.Utc), zone);
        var afterFallTransition = TimeZoneInfo.ConvertTimeFromUtc(
            new DateTime(2026, 11, 1, 6, 1, 0, DateTimeKind.Utc), zone);

        Assert.Equal(new DateOnly(2026, 3, 8), DateOnly.FromDateTime(beforeSpringTransition));
        Assert.Equal(new DateOnly(2026, 3, 8), DateOnly.FromDateTime(afterSpringTransition));
        Assert.Equal(new DateOnly(2026, 11, 1), DateOnly.FromDateTime(beforeFallTransition));
        Assert.Equal(new DateOnly(2026, 11, 1), DateOnly.FromDateTime(afterFallTransition));
        Assert.True(ElderlyTimeZone.IsValid("America/New_York"));
        Assert.False(ElderlyTimeZone.IsValid("Mars/Olympus"));
    }

    private static FamiliesDbContext CreateDb(out Elderly elderly, out UserId actor)
    {
        var db = new FamiliesDbContext(new DbContextOptionsBuilder<FamiliesDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        actor = UserId.New();
        var family = Family.Create(UserId.New());
        elderly = Elderly.Create(family.OwnerUserId, actor, family.Id, FamilyRelationshipType.Father,
            FullName.Create("الاسم"), FullName.Create("Elderly"), Gender.Male,
            new DateOnly(1950, 1, 1), new DateOnly(2026, 1, 1));
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.SaveChanges();
        return db;
    }

    private sealed class RecordingAlerts : IElderlyCheckInAlertGateway
    {
        public List<ElderlyCheckInAlertRequest> Requests { get; } = [];

        public Task<int> CreateNegativeCheckInAlertsAsync(ElderlyCheckInAlertRequest elderly, DateTime createdOnUtc, CancellationToken cancellationToken = default)
        {
            Requests.Add(elderly);
            return Task.FromResult(1);
        }
    }
}
