using Microsoft.EntityFrameworkCore;
using Sanad.BuildingBlocks.Domain.Enums;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Families.Application.Abstractions.HelpRequests;
using Sanad.Modules.Families.Application.Abstractions.Data;
using Sanad.Modules.Families.Application.Abstractions.Identity;
using Sanad.Modules.Families.Application.HelpRequests;
using Sanad.Modules.Families.Domain.Elderlies;
using Sanad.Modules.Families.Domain.Families;
using Sanad.Modules.Families.Domain.HelpRequests;
using Sanad.Modules.Families.Infrastructure.Persistence;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.Families.HelpRequests;

public sealed class ElderlyHelpRequestTests
{
    [Fact]
    public void Domain_Create_RejectsMissingIdentityIdempotencyAndUnsafeCustomText()
    {
        var family = Guid.NewGuid();
        Assert.ThrowsAny<Exception>(() => ElderlyHelpRequest.Create(UserId.Empty, family, "a", "ع", "a", "b", "ف", "b", "c", "ح", "c", null, null, null, null, "key"));
        Assert.ThrowsAny<Exception>(() => ElderlyHelpRequest.Create(UserId.New(), family, "a", "ع", "a", "b", "ف", "b", "c", "ح", "c", null, null, null, null, ""));
        Assert.ThrowsAny<Exception>(() => ElderlyHelpRequest.Create(UserId.New(), family, "a", "ع", "a", "b", "ف", "b", "c", "ح", "c", null, null, null, "<script>", "key"));
        Assert.ThrowsAny<Exception>(() => ElderlyHelpRequest.Create(UserId.New(), family, "a", "ع", "a", "b", "ف", "b", "c", "ح", "c", null, null, null, new string('x', 501), "key"));
    }

    [Fact]
    public void Domain_TransitionsAreAppendOnly_AndReopenReturnsToInProgress()
    {
        var actor = UserId.New();
        var request = CreateRequest(actor);
        request.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, actor);
        request.Transition(ElderlyHelpRequestHistoryAction.Started, null, actor);
        request.Transition(ElderlyHelpRequestHistoryAction.Resolved, "done", actor);
        request.Transition(ElderlyHelpRequestHistoryAction.Reopened, "needs follow-up", actor);

        Assert.Equal(ElderlyHelpRequestStatus.InProgress, request.Status);
        Assert.Equal(5, request.History.Count);
        Assert.Equal(ElderlyHelpRequestHistoryAction.Reopened, request.History.Last().Action);
        Assert.Equal(ElderlyHelpRequestStatus.Reopened, request.History.Last().Status);
        Assert.ThrowsAny<Exception>(() => request.Transition(ElderlyHelpRequestHistoryAction.Resolved, null, actor));
        Assert.ThrowsAny<Exception>(() => request.Transition(ElderlyHelpRequestHistoryAction.Rejected, "", actor));
    }

    [Fact]
    public async Task Create_IsIdentityBound_ValidatesActiveCatalog_ReplaysSamePayloadAndRejectsConflict()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var identity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, identity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family); db.Elderlies.Add(elderly); await db.SaveChangesAsync();
        var catalog = new CatalogGateway(new[]
        {
            new HelpRequestCatalogItem("elderly", SentenceBuilderCatalogCategory.Actor, "مسن", "Elderly"),
            new HelpRequestCatalogItem("needs", SentenceBuilderCatalogCategory.Need, "مساعدة", "Help"),
            new HelpRequestCatalogItem("ask", SentenceBuilderCatalogCategory.Action, "يطلب", "asks"),
            new HelpRequestCatalogItem("urgent", SentenceBuilderCatalogCategory.Qualifier, "عاجل", "urgent")
        });
        var notifications = new RecordingNotifications();
        var handler = new CreateElderlyHelpRequestCommandHandler(db, catalog, notifications);
        var command = new CreateElderlyHelpRequestCommand(identity, "elderly", "ask", "needs", "urgent", "  please help  ", "idem-1");

        var first = await handler.Handle(command, default);
        var replay = await handler.Handle(command, default);
        var conflict = await handler.Handle(command with { CustomText = "different" }, default);
        var wrongIdentity = await handler.Handle(command with { ElderlyIdentityUserId = UserId.New(), IdempotencyKey = "idem-2" }, default);

        Assert.True(first.IsSuccess, first.IsFailure ? first.Error.Code : null);
        Assert.True(replay.IsSuccess, replay.IsFailure ? replay.Error.Code : null);
        Assert.Equal(first.Value.Id, replay.Value.Id);
        Assert.Equal("please help", first.Value.CustomText);
        Assert.True(conflict.IsFailure);
        Assert.Equal("Families.HelpRequest.IdempotencyConflict", conflict.Error.Code);
        Assert.True(wrongIdentity.IsFailure);
        Assert.Equal("Families.HelpRequest.NotFound", wrongIdentity.Error.Code);
        Assert.Single(await db.ElderlyHelpRequests.ToListAsync());
        Assert.Single(notifications.RequestIds);
    }

    [Fact]
    public async Task Cancel_IsLimitedToPendingRequests_AndRequiresReason()
    {
        await using var db = CreateDb();
        var actor = UserId.New(); var owner = UserId.New(); var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, actor, family.Id, FamilyRelationshipType.Father, FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        db.Families.Add(family); db.Elderlies.Add(elderly); var request = CreateRequest(actor); db.ElderlyHelpRequests.Add(request); await db.SaveChangesAsync();

        var cancelled = await new CancelElderlyHelpRequestCommandHandler(db).Handle(new(actor, request.Id, null), default);
        Assert.True(cancelled.IsSuccess);
        Assert.Equal(ElderlyHelpRequestStatus.Cancelled, cancelled.Value.Status);
        Assert.Equal(ElderlyHelpRequestHistoryAction.Cancelled, request.History.Last().Action);
    }

    [Fact]
    public async Task AdminReads_AuditBeforeRead_FilterActiveFamilies_AndProjectWithPagingAndStatus()
    {
        await using var db = CreateDb();
        var actor = UserId.New();
        var activeOwner = UserId.New();
        var activeFamily = Family.Create(activeOwner, "active");
        var activeElderly = Elderly.Create(activeOwner, UserId.New(), activeFamily.Id, FamilyRelationshipType.Father, FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var deletedOwner = UserId.New();
        var deletedFamily = Family.Create(deletedOwner, "deleted");
        deletedFamily.MarkDeleted("closed", "test");
        var deletedElderly = Elderly.Create(deletedOwner, UserId.New(), deletedFamily.Id, FamilyRelationshipType.Father, FullName.Create("محذوف"), FullName.Create("Deleted"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var activeRequest = CreateRequest(activeElderly.IdentityUserId);
        var deletedRequest = CreateRequest(deletedElderly.IdentityUserId);
        activeRequest.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, actor);
        db.Families.AddRange(activeFamily, deletedFamily); db.Elderlies.AddRange(activeElderly, deletedElderly); db.ElderlyHelpRequests.AddRange(activeRequest, deletedRequest); await db.SaveChangesAsync();

        var list = await new AdminListElderlyHelpRequestsQueryHandler(db).Handle(new(actor, AccountType.SupportAdmin.ToString(), "list", ElderlyHelpRequestStatus.Accepted, activeElderly.Id.Value, 0, 1), default);
        var detail = await new AdminGetElderlyHelpRequestQueryHandler(db).Handle(new(actor, AccountType.SuperAdmin.ToString(), "detail", activeRequest.Id), default);
        var history = await new AdminGetElderlyHelpRequestHistoryQueryHandler(db).Handle(new(actor, AccountType.SupportAdmin.ToString(), "history", activeRequest.Id), default);
        var aggregate = await new AdminGetElderlyHelpRequestAggregateQueryHandler(db).Handle(new(actor, AccountType.SupportAdmin.ToString(), "aggregate"), default);
        var deletedDetail = await new AdminGetElderlyHelpRequestQueryHandler(db).Handle(new(actor, AccountType.SupportAdmin.ToString(), "deleted", deletedRequest.Id), default);

        Assert.True(list.IsSuccess);
        Assert.Single(list.Value.Items);
        Assert.Equal(1, list.Value.Page);
        Assert.Equal(1, list.Value.PageSize);
        Assert.Equal(1, list.Value.TotalCount);
        Assert.Equal(activeElderly.Id.Value, list.Value.Items[0].ElderlyId);
        Assert.Equal(activeRequest.Id, detail.Value.Id);
        Assert.Equal(2, history.Value.Count);
        Assert.Equal(1, aggregate.Value[ElderlyHelpRequestStatus.Accepted]);
        Assert.False(deletedDetail.IsSuccess);
        Assert.Equal(5, await db.AdminMedicationAccessAudits.CountAsync());
        Assert.Equal("GetHelpRequest", (await db.AdminMedicationAccessAudits.OrderByDescending(x => x.OccurredOnUtc).FirstAsync()).Action);
    }

    [Fact]
    public async Task Detail_ReturnsSingleCallerOwnedRequest_AndNotFoundForUnknownOrForeignIds()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var identity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, identity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var request = CreateRequest(identity);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyHelpRequests.Add(request);
        await db.SaveChangesAsync();
        var handler = new GetElderlyHelpRequestQueryHandler(db);

        var found = await handler.Handle(new(identity, request.Id), default);
        var missing = await handler.Handle(new(identity, Guid.NewGuid()), default);
        var foreign = await handler.Handle(new(UserId.New(), request.Id), default);

        Assert.True(found.IsSuccess);
        Assert.Equal(request.Id, found.Value.Id);
        Assert.Equal(elderly.Id.Value, found.Value.ElderlyId);
        Assert.True(missing.IsFailure);
        Assert.Equal("Families.HelpRequest.NotFound", missing.Error.Code);
        Assert.True(foreign.IsFailure);
        Assert.Equal("Families.HelpRequest.NotFound", foreign.Error.Code);
    }

    private static ElderlyHelpRequest CreateRequest(UserId actor) => ElderlyHelpRequest.Create(actor, Guid.NewGuid(), "elderly", "مسن", "Elderly", "ask", "يطلب", "asks", "needs", "مساعدة", "Help", null, null, null, null, Guid.NewGuid().ToString());
    private static FamiliesDbContext CreateDb() => new(new DbContextOptionsBuilder<FamiliesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class CatalogGateway(IReadOnlyList<HelpRequestCatalogItem> items) : IHelpRequestCatalogGateway
    { public Task<IReadOnlyList<HelpRequestCatalogItem>> GetActiveAsync(IEnumerable<string> keys, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<HelpRequestCatalogItem>>(items.Where(x => keys.Contains(x.Key)).ToArray()); }
    private sealed class RecordingNotifications : IHelpRequestNotificationGateway
    { public List<Guid> RequestIds { get; } = []; public Task NotifyCreatedAsync(UserId elderlyIdentityUserId, Guid elderlyId, Guid helpRequestId, CancellationToken cancellationToken = default) { RequestIds.Add(helpRequestId); return Task.CompletedTask; }
    }

    [Fact]
    public async Task AcknowledgeFamilyDependentHelpRequest_Success()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var request = CreateRequest(elderlyIdentity);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyHelpRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeFamilyDependentHelpRequestCommandHandler(db);

        var command = new AcknowledgeFamilyDependentHelpRequestCommand(owner, elderly.Id, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsSuccess);
        Assert.Equal(ElderlyHelpRequestStatus.Acknowledged, result.Value.Status);
        Assert.Equal(request.Id, result.Value.Id);
        Assert.Equal(elderly.Id.Value, result.Value.ElderlyId);
        Assert.Equal(2, db.ElderlyHelpRequestHistories.Count());
        Assert.Equal(ElderlyHelpRequestHistoryAction.Acknowledged, db.ElderlyHelpRequestHistories.Last().Action);
        Assert.Equal(ElderlyHelpRequestStatus.Acknowledged, db.ElderlyHelpRequestHistories.Last().Status);
    }

    [Fact]
    public async Task AcknowledgeFamilyDependentHelpRequest_FailsWhenNotFamilyMember()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var request = CreateRequest(elderlyIdentity);
        var nonFamilyMember = UserId.New();
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyHelpRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeFamilyDependentHelpRequestCommandHandler(db);

        var command = new AcknowledgeFamilyDependentHelpRequestCommand(nonFamilyMember, elderly.Id, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Elderly.FamilyNotFound", result.Error.Code);
    }

    [Fact]
    public async Task AcknowledgeFamilyDependentHelpRequest_FailsWhenDependentNotInFamily()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var request = CreateRequest(elderlyIdentity);
        var otherFamily = Family.Create(UserId.New(), "other family");
        var otherElderly = Elderly.Create(UserId.New(), UserId.New(), otherFamily.Id, FamilyRelationshipType.Father,
            FullName.Create("أحمد"), FullName.Create("Ahmed"), Gender.Male, new DateOnly(1960, 1, 1), new DateOnly(2021, 1, 1));
        db.Families.AddRange(family, otherFamily);
        db.Elderlies.AddRange(elderly, otherElderly);
        db.ElderlyHelpRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeFamilyDependentHelpRequestCommandHandler(db);

        var command = new AcknowledgeFamilyDependentHelpRequestCommand(owner, otherElderly.Id, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.Elderly.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AcknowledgeFamilyDependentHelpRequest_FailsWhenHelpRequestNotFound()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var request = CreateRequest(elderlyIdentity);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyHelpRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeFamilyDependentHelpRequestCommandHandler(db);

        var command = new AcknowledgeFamilyDependentHelpRequestCommand(owner, elderly.Id, Guid.NewGuid());
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.HelpRequest.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task AcknowledgeFamilyDependentHelpRequest_FailsWhenHelpRequestNotPending()
    {
        await using var db = CreateDb();
        var owner = UserId.New();
        var elderlyIdentity = UserId.New();
        var family = Family.Create(owner, "family");
        var elderly = Elderly.Create(owner, elderlyIdentity, family.Id, FamilyRelationshipType.Father,
            FullName.Create("عمر"), FullName.Create("Omar"), Gender.Male, new DateOnly(1950, 1, 1), new DateOnly(2020, 1, 1));
        var request = CreateRequest(elderlyIdentity);
        // Accept the request first
        request.Transition(ElderlyHelpRequestHistoryAction.Accepted, null, elderlyIdentity);
        db.Families.Add(family);
        db.Elderlies.Add(elderly);
        db.ElderlyHelpRequests.Add(request);
        await db.SaveChangesAsync();

        var handler = new AcknowledgeFamilyDependentHelpRequestCommandHandler(db);

        var command = new AcknowledgeFamilyDependentHelpRequestCommand(owner, elderly.Id, request.Id);
        var result = await handler.Handle(command, default);

        Assert.True(result.IsFailure);
        Assert.Equal("Families.HelpRequest.InvalidOperation", result.Error.Code);
    }

}
