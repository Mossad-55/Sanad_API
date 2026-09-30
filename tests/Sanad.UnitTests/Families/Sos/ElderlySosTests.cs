using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Sos;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.Sos;
using Sanad.Modules.Families.Infrastructure.Persistence;

namespace Sanad.UnitTests.Families.Sos;

public sealed class ElderlySosTests
{
    [Fact]
    public void Domain_Create_EnforcesIdentityConsentBoundsPrecisionAndHistory()
    {
        var identity = UserId.New();
        var sos = ElderlySos.Create(identity, Guid.NewGuid(), " key ", true, 30.12345m, 31.98765m);
        Assert.Equal("key", sos.IdempotencyKey);
        Assert.Equal(30.123m, sos.Latitude); Assert.Equal(31.988m, sos.Longitude);
        Assert.Equal(ElderlySosStatus.Open, sos.Status); Assert.Single(sos.History);
        Assert.ThrowsAny<Exception>(() => ElderlySos.Create(UserId.Empty, Guid.NewGuid(), "key", false, null, null));
        Assert.ThrowsAny<Exception>(() => ElderlySos.Create(identity, Guid.NewGuid(), "key", false, 1m, 2m));
        Assert.ThrowsAny<Exception>(() => ElderlySos.Create(identity, Guid.NewGuid(), "key", true, 90.001m, 2m));
        Assert.ThrowsAny<Exception>(() => ElderlySos.Create(identity, Guid.NewGuid(), "key", true, 1m, 180.001m));
    }

    [Fact]
    public async Task Create_IsIdentityBound_ConsentGated_RoundsAndReplaysOrConflicts()
    {
        await using var db = CreateDb(); var owner = UserId.New(); var identity = UserId.New();
        var family = Family.Create(owner, "active"); var elderly = MakeElderly(owner, identity, family.Id);
        db.Families.Add(family); db.Elderlies.Add(elderly); await db.SaveChangesAsync();
        var notifications = new RecordingNotifications(); var handler = new CreateElderlySosCommandHandler(db, notifications);
        var command = new CreateElderlySosCommand(identity, " sos-1 ", true, 10.12349m, 20.98761m);
        var first = await handler.Handle(command, default);
        var replay = await handler.Handle(command with { Latitude = 10.1234m, Longitude = 20.9876m }, default);
        var conflict = await handler.Handle(command with { Longitude = 20.987m }, default);
        var wrongDependent = await handler.Handle(command with { ElderlyIdentityUserId = UserId.New(), IdempotencyKey = "sos-2" }, default);
        var noConsent = await handler.Handle(command with { IdempotencyKey = "sos-3", LocationConsentGranted = false }, default);
        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Code : null); Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error.Code : null);
        Assert.Equal(first.Value.Id, replay.Value.Id); Assert.Equal(10.123m, first.Value.Latitude); Assert.Equal(20.988m, first.Value.Longitude);
        Assert.Equal("Families.Sos.IdempotencyConflict", conflict.Error.Code); Assert.Equal("Families.Sos.NotFound", wrongDependent.Error.Code);
        Assert.Equal("Families.Sos.InvalidLocation", noConsent.Error.Code); Assert.Single(await db.ElderlySos.ToListAsync()); Assert.Equal(2, notifications.SosIds.Count);
    }

    [Fact]
    public async Task Lifecycle_History_AllowsOnlyContractedTransitions()
    {
        await using var db = CreateDb(); var owner = UserId.New(); var identity = UserId.New(); var admin = UserId.New();
        var family = Family.Create(owner, "active"); var elderly = MakeElderly(owner, identity, family.Id);
        db.Families.Add(family); db.Elderlies.Add(elderly); var sos = ElderlySos.Create(identity, family.Id.Value, "lifecycle", false, null, null); db.ElderlySos.Add(sos); await db.SaveChangesAsync();
        var acknowledged = await new ChangeElderlySosStatusCommandHandler(db).Handle(new(admin, "SupportAdmin", "c1", sos.Id, ElderlySosHistoryAction.Acknowledged), default);
        var resolved = await new ChangeElderlySosStatusCommandHandler(db).Handle(new(admin, "SupportAdmin", "c2", sos.Id, ElderlySosHistoryAction.Resolved), default);
        var invalid = await new ChangeElderlySosStatusCommandHandler(db).Handle(new(admin, "SupportAdmin", "c3", sos.Id, ElderlySosHistoryAction.Acknowledged), default);
        var cancelled = await new CancelElderlySosCommandHandler(db).Handle(new(identity, sos.Id), default);
        Assert.True(acknowledged.IsSuccess); Assert.True(resolved.IsSuccess); Assert.Equal("Families.Sos.InvalidOperation", invalid.Error.Code); Assert.Equal("Families.Sos.InvalidOperation", cancelled.Error.Code);
        Assert.Equal(new[] { ElderlySosHistoryAction.Created, ElderlySosHistoryAction.Acknowledged, ElderlySosHistoryAction.Resolved }, sos.History.Select(x => x.Action));
    }

    [Fact]
    public async Task Reads_FilterPreciseLocationAtThirtyDays_AndIsolateDeletedFamilies()
    {
        await using var db = CreateDb(); var owner = UserId.New(); var identity = UserId.New(); var family = Family.Create(owner, "active"); var elderly = MakeElderly(owner, identity, family.Id);
        var old = ElderlySos.Create(identity, family.Id.Value, "old", true, 1.2345m, 2.3456m); var current = ElderlySos.Create(identity, family.Id.Value, "current", true, 3.4567m, 4.5678m);
        db.Families.Add(family); db.Elderlies.Add(elderly); db.ElderlySos.AddRange(old, current); await db.SaveChangesAsync(); db.Entry(old).Property(x => x.CreatedOnUtc).CurrentValue = DateTime.UtcNow.AddDays(-30).AddSeconds(-1); await db.SaveChangesAsync();
        var rows = await new GetElderlySosQueryHandler(db).Handle(new(identity), default); Assert.True(rows.IsSuccess); Assert.Null(rows.Value.Single(x => x.Id == old.Id).Latitude); Assert.Equal(3.457m, rows.Value.Single(x => x.Id == current.Id).Latitude);
        family.MarkDeleted("closed", "test"); await db.SaveChangesAsync(); var hidden = await new GetElderlySosQueryHandler(db).Handle(new(identity), default); Assert.Equal("Families.Sos.NotFound", hidden.Error.Code);
    }

    [Fact]
    public async Task AdminReads_AuditBeforeRead_PageAndFilterActiveFamilies()
    {
        await using var db = CreateDb(); var owner = UserId.New(); var identity = UserId.New(); var actor = UserId.New(); var family = Family.Create(owner, "active"); var elderly = MakeElderly(owner, identity, family.Id); var sos = ElderlySos.Create(identity, family.Id.Value, "admin", false, null, null);
        db.Families.Add(family); db.Elderlies.Add(elderly); db.ElderlySos.Add(sos); await db.SaveChangesAsync();
        var list = await new AdminListElderlySosQueryHandler(db).Handle(new(actor, "SupportAdmin", "c1", Page: 0, PageSize: 1), default); var detail = await new AdminGetElderlySosQueryHandler(db).Handle(new(actor, "SupportAdmin", "c2", sos.Id), default); var history = await new AdminGetElderlySosHistoryQueryHandler(db).Handle(new(actor, "SupportAdmin", "c3", sos.Id), default); var missing = await new AdminGetElderlySosHistoryQueryHandler(db).Handle(new(actor, "SupportAdmin", "c4", Guid.NewGuid()), default);
        Assert.Single(list.Value.Items); Assert.Equal(1, list.Value.Page); Assert.Equal(1, list.Value.PageSize); Assert.Equal(sos.Id, detail.Value.Id); Assert.Single(history.Value); Assert.Equal("Families.Sos.NotFound", missing.Error.Code);
        Assert.Equal(new[] { "ListSos", "GetSos", "GetSosHistory", "GetSosHistory" }, await db.AdminMedicationAccessAudits.OrderBy(x => x.OccurredOnUtc).Select(x => x.Action).ToListAsync());
    }

    [Fact]
    public async Task Detail_ReturnsOneCallerOwnedSos_AndNotFoundForUnknownOrForeignIds()
    {
        await using var db = CreateDb();
        var owner = UserId.New(); var identity = UserId.New(); var family = Family.Create(owner, "active");
        var elderly = MakeElderly(owner, identity, family.Id);
        var sos = ElderlySos.Create(identity, family.Id.Value, "detail", false, null, null);
        db.Families.Add(family); db.Elderlies.Add(elderly); db.ElderlySos.Add(sos); await db.SaveChangesAsync();
        var handler = new GetElderlySosDetailQueryHandler(db);

        var found = await handler.Handle(new(identity, sos.Id), default);
        var missing = await handler.Handle(new(identity, Guid.NewGuid()), default);
        var foreign = await handler.Handle(new(UserId.New(), sos.Id), default);

        Assert.True(found.IsSuccess); Assert.Equal(sos.Id, found.Value.Id); Assert.Equal(elderly.Id.Value, found.Value.ElderlyId);
        Assert.Equal("Families.Sos.NotFound", missing.Error.Code); Assert.Equal("Families.Sos.NotFound", foreign.Error.Code);
    }

    private static Elderly MakeElderly(UserId owner, UserId identity, FamilyId familyId) => Elderly.Create(owner, identity, familyId, FamilyRelationshipType.Father, FullName.Create("Omar"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), DateOnly.FromDateTime(DateTime.UtcNow));
    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private sealed class RecordingNotifications : Sanad.Modules.Families.Application.Abstractions.Sos.ISosNotificationGateway { public List<Guid> SosIds { get; } = []; public Task NotifyCreatedAsync(UserId identity, Guid elderlyId, Guid sosId, CancellationToken ct = default) { SosIds.Add(sosId); return Task.CompletedTask; } }
}
