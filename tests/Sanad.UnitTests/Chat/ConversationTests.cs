using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.ChatIntegration;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Exceptions;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Attachments;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.UnitTests.Chat;

public sealed class ConversationTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 1, 12, 0, 0), DateTimeKind.Utc);

    [Fact]
    public void Create_NormalizesParticipantPairSoEitherDirectionMatchesKey()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();

        Conversation conversation = Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.True(conversation.MatchesKey(userA, userB, null));
        Assert.True(conversation.MatchesKey(userB, userA, null));
        Assert.Equal(userA.Value.CompareTo(userB.Value) <= 0
            ? userA
            : userB, conversation.ParticipantOne);
        Assert.Equal(userA.Value.CompareTo(userB.Value) <= 0
            ? userB
            : userA, conversation.ParticipantTwo);
    }

    [Fact]
    public void Create_KeysOpenOrGetOnPairPlusBookingReference()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        BookingId bookingOne = BookingId.New();
        BookingId bookingTwo = BookingId.New();

        Conversation direct = Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            null,
            UtcStamp);
        Conversation bookingScoped = Conversation.Create(
            userA,
            userB,
            ConversationScope.Booking,
            bookingOne,
            UtcStamp);

        Assert.True(direct.MatchesKey(userA, userB, null));
        Assert.False(direct.MatchesKey(userA, userB, bookingOne));

        Assert.True(bookingScoped.MatchesKey(userB, userA, bookingOne));
        Assert.False(bookingScoped.MatchesKey(userA, userB, null));
        Assert.False(bookingScoped.MatchesKey(userA, userB, bookingTwo));

        Assert.NotEqual(direct.Id, bookingScoped.Id);
        Assert.Equal(ConversationScope.Direct, direct.Scope);
        Assert.Equal(ConversationScope.Booking, bookingScoped.Scope);
        Assert.Equal(bookingOne, bookingScoped.BookingId);
    }

    [Fact]
    public void Create_SetsLastActivityFromCreationAndKeepsUtc()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.Equal(UtcStamp, conversation.CreatedOnUtc);
        Assert.Equal(UtcStamp, conversation.LastActivityOnUtc);
        Assert.Equal(DateTimeKind.Utc, conversation.LastActivityOnUtc.Kind);
    }

    [Fact]
    public void Create_RejectsSelfConversation()
    {
        UserId user = UserId.New();

        Assert.Throws<DomainException>(() => Conversation.Create(
            user,
            user,
            ConversationScope.Direct,
            null,
            UtcStamp));
    }

    [Fact]
    public void Create_RejectsMissingParticipant()
    {
        Assert.Throws<DomainException>(() => Conversation.Create(
            UserId.Empty,
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp));
    }

    [Fact]
    public void Create_RejectsInconsistentScopeAndBookingReference()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        BookingId bookingId = BookingId.New();

        Assert.Throws<DomainException>(() => Conversation.Create(
            userA,
            userB,
            ConversationScope.Booking,
            null,
            UtcStamp));

        Assert.Throws<DomainException>(() => Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            bookingId,
            UtcStamp));
    }

    [Fact]
    public void Create_RejectsNonUtcTimestamp()
    {
        DateTime localStamp = DateTime.SpecifyKind(
            new DateTime(2026, 10, 1, 12, 0, 0),
            DateTimeKind.Local);

        Assert.Throws<DomainException>(() => Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            localStamp));
    }

    [Fact]
    public void UpdateLastActivity_RejectsNonUtcTimestamp()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.Throws<DomainException>(() => conversation.UpdateLastActivity(
            new DateTime(2026, 10, 1, 13, 0, 0, DateTimeKind.Unspecified)));
        Assert.Equal(UtcStamp, conversation.LastActivityOnUtc);
    }

    [Fact]
    public void Hide_HidesForDeleterOnlyAndErasesNothing()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        Conversation conversation = Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            null,
            UtcStamp);

        conversation.Hide(userA);

        Assert.True(conversation.IsHiddenFor(userA));
        Assert.False(conversation.IsHiddenFor(userB));
        Assert.True(conversation.IsParticipant(userA));
        Assert.True(conversation.IsParticipant(userB));
        Assert.Equal(UtcStamp, conversation.CreatedOnUtc);
        Assert.Equal(UtcStamp, conversation.LastActivityOnUtc);
    }

    [Fact]
    public void Hide_ByNonParticipantThrows()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);
        UserId outsider = UserId.New();

        Assert.Throws<DomainException>(() => conversation.Hide(outsider));
        Assert.Throws<DomainException>(() => conversation.IsHiddenFor(outsider));
    }

    [Fact]
    public void Attachment_CreateNormalizesKeyAndRejectsInvalidInput()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);
        UserId uploader = UserId.New();

        Attachment attachment = Attachment.Create(
            conversation.Id,
            uploader,
            "  private/chat/file.webp  ",
            "  IMAGE/WEBP  ",
            2048,
            UtcStamp);

        Assert.Equal(conversation.Id, attachment.ConversationId);
        Assert.Equal(uploader, attachment.UploadedBy);
        Assert.Equal("private/chat/file.webp", attachment.StorageKey);
        Assert.Equal("image/webp", attachment.ContentType);
        Assert.Equal(2048, attachment.SizeBytes);
        Assert.False(attachment.IsBound);

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            uploader,
            " ",
            "image/webp",
            2048,
            UtcStamp));

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            UserId.Empty,
            "private/chat/file.webp",
            "image/webp",
            2048,
            UtcStamp));

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            uploader,
            "private/chat/file.webp",
            "image/webp",
            0,
            UtcStamp));
    }

    [Fact]
    public void Attachment_CreateRejectsNonUtcTimestamp()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);

        Assert.Throws<DomainException>(() => Attachment.Create(
            conversation.Id,
            UserId.New(),
            "private/chat/file.jpg",
            "image/jpeg",
            100,
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Local)));
    }

    [Fact]
    public void Attachment_BindIsSingleUse()
    {
        Conversation conversation = Conversation.Create(
            UserId.New(),
            UserId.New(),
            ConversationScope.Direct,
            null,
            UtcStamp);
        Attachment attachment = Attachment.Create(
            conversation.Id,
            UserId.New(),
            "private/chat/file.png",
            "image/png",
            512,
            UtcStamp);

        attachment.Bind();
        Assert.True(attachment.IsBound);

        Assert.Throws<DomainException>(() => attachment.Bind());
        Assert.True(attachment.IsBound);
    }

    [Fact]
    public void Block_CreateKeepsOrderedPairAndRejectsSelfBlock()
    {
        UserId blocker = UserId.New();
        UserId blocked = UserId.New();

        Block block = Block.Create(blocker, blocked, UtcStamp);

        Assert.Equal(blocker, block.BlockerUserId);
        Assert.Equal(blocked, block.BlockedUserId);
        Assert.Equal(UtcStamp, block.CreatedOnUtc);

        Assert.Throws<DomainException>(() => Block.Create(
            blocker,
            blocker,
            UtcStamp));
    }

    [Fact]
    public void Block_CreateRejectsNonUtcTimestamp()
    {
        Assert.Throws<DomainException>(() => Block.Create(
            UserId.New(),
            UserId.New(),
            new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Unspecified)));
    }

    [Fact]
    public void Unhide_RestoresOnlyThatParticipantAndRejectsOutsiders()
    {
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        Conversation conversation = Conversation.Create(
            userA,
            userB,
            ConversationScope.Direct,
            null,
            UtcStamp);
        conversation.Hide(userA);

        conversation.Unhide(userA);

        Assert.False(conversation.IsHiddenFor(userA));
        Assert.False(conversation.IsHiddenFor(userB));
        Assert.Throws<DomainException>(() => conversation.Unhide(UserId.New()));
    }

    // ------------------------------------------------------------------
    // Endpoint 1 / 2 / 10 — handler coverage over an EF InMemory
    // ChatDbContext with faked outbound ports. InMemory does not enforce
    // the unique indexes, so open-or-get idempotency is proven at handler
    // level rather than by the database.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Open_CreatesOnceThenReturnsTheSameIdWithoutDuplicating()
    {
        await using ChatDbContext db = CreateDb();
        UserId opener = UserId.New();
        UserId recipient = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(recipient, "Recipient");
        var handler = new OpenConversationCommandHandler(
            db, new ApiChatEligibilityChecker(lookup));
        var command = new OpenConversationCommand(opener, recipient, null);

        Result<OpenConversationResponse> first =
            await handler.Handle(command, default);
        Result<OpenConversationResponse> second =
            await handler.Handle(command, default);

        Assert.True(first.Value.Created);
        Assert.False(second.Value.Created);
        Assert.Equal(first.Value.ConversationId, second.Value.ConversationId);
        Assert.Equal(1, await db.Conversations.CountAsync());
    }

    [Fact]
    public async Task Open_RestoresTheOpenersVisibilityAfterAHideWithoutTouchingTheOtherParty()
    {
        await using ChatDbContext db = CreateDb();
        UserId opener = UserId.New();
        UserId recipient = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(recipient, "Recipient");
        var openHandler = new OpenConversationCommandHandler(
            db, new ApiChatEligibilityChecker(lookup));
        var hideHandler = new HideConversationCommandHandler(db);

        OpenConversationResponse opened = (await openHandler.Handle(
            new OpenConversationCommand(opener, recipient, null), default)).Value;
        ConversationId conversationId = new(opened.ConversationId);

        Assert.True((await hideHandler.Handle(
            new HideConversationCommand(opener, conversationId), default)).IsSuccess);

        Conversation stored = await db.Conversations.SingleAsync();
        DateTime lastActivity = stored.LastActivityOnUtc;
        Assert.True(stored.IsHiddenFor(opener));
        Assert.False(stored.IsHiddenFor(recipient));

        OpenConversationResponse reopened = (await openHandler.Handle(
            new OpenConversationCommand(opener, recipient, null), default)).Value;

        Assert.Equal(opened.ConversationId, reopened.ConversationId);
        Assert.False(reopened.Created);

        Conversation reloaded = await db.Conversations.AsNoTracking().SingleAsync();
        Assert.False(reloaded.IsHiddenFor(opener));
        Assert.False(reloaded.IsHiddenFor(recipient));
        Assert.Equal(lastActivity, reloaded.LastActivityOnUtc);
    }

    [Fact]
    public async Task Open_UnknownRecipientFailsWithThePrivacyCodeAndPersistsNothing()
    {
        await using ChatDbContext db = CreateDb();
        var handler = new OpenConversationCommandHandler(
            db, new ApiChatEligibilityChecker(new FakeChatUserLookup()));

        var result = await handler.Handle(
            new OpenConversationCommand(UserId.New(), UserId.New(), null),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(ConversationErrors.NotFound.Code, result.Error.Code);
        Assert.Equal(0, await db.Conversations.CountAsync());
    }

    [Fact]
    public async Task Open_KeepsDirectAndBookingScopedRowsAsSeparateKeys()
    {
        await using ChatDbContext db = CreateDb();
        UserId opener = UserId.New();
        UserId recipient = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(recipient, "Recipient");
        var handler = new OpenConversationCommandHandler(
            db, new ApiChatEligibilityChecker(lookup));
        BookingId bookingId = BookingId.New();

        OpenConversationResponse direct = (await handler.Handle(
            new OpenConversationCommand(opener, recipient, null), default)).Value;
        OpenConversationResponse booking = (await handler.Handle(
            new OpenConversationCommand(opener, recipient, bookingId), default)).Value;
        OpenConversationResponse directAgain = (await handler.Handle(
            new OpenConversationCommand(opener, recipient, null), default)).Value;

        Assert.True(direct.Created);
        Assert.True(booking.Created);
        Assert.False(directAgain.Created);
        Assert.Equal(direct.ConversationId, directAgain.ConversationId);
        Assert.NotEqual(direct.ConversationId, booking.ConversationId);
        Assert.Equal(2, await db.Conversations.CountAsync());
    }

    [Fact]
    public async Task GetConversations_ListsOnlyTheRequestersVisibleRowsAndHidingIsPerUser()
    {
        await using ChatDbContext db = CreateDb();
        UserId requester = UserId.New();
        UserId peerOne = UserId.New();
        UserId peerTwo = UserId.New();
        UserId peerThree = UserId.New();

        Conversation hidden = CreateDirect(requester, peerOne, UtcStamp);
        Conversation visible = CreateDirect(requester, peerTwo, UtcStamp);
        Conversation alsoVisible = CreateDirect(requester, peerThree, UtcStamp);
        Conversation foreign = CreateDirect(peerOne, peerTwo, UtcStamp);
        db.Conversations.AddRange(hidden, visible, alsoVisible, foreign);
        await db.SaveChangesAsync();

        Assert.True((await new HideConversationCommandHandler(db).Handle(
            new HideConversationCommand(requester, hidden.Id), default)).IsSuccess);

        var lookup = new FakeChatUserLookup();
        lookup.Add(peerTwo, "Peer Two", "avatar-2");
        var handler = new GetConversationsQueryHandler(db, lookup);

        PagedResult<ConversationListItemResponse> mine = (await handler.Handle(
            new GetConversationsQuery(requester), default)).Value;
        PagedResult<ConversationListItemResponse> theirs = (await handler.Handle(
            new GetConversationsQuery(peerOne), default)).Value;

        Assert.Equal(2, mine.TotalCount);
        Assert.DoesNotContain(mine.Items, item => item.Id == hidden.Id.Value);
        Assert.Contains(mine.Items, item => item.Id == visible.Id.Value);

        // The requester's hide never touches the other party's copy, and a
        // conversation between two other users is never listed.
        Assert.Equal(2, theirs.TotalCount);
        Assert.Contains(theirs.Items, item => item.Id == hidden.Id.Value);
        Assert.DoesNotContain(theirs.Items, item => item.Id == alsoVisible.Id.Value);
    }

    [Fact]
    public async Task GetConversations_OrdersByLastActivityDesc_ClampsPagingAndReturnsMessageStubs()
    {
        await using ChatDbContext db = CreateDb();
        UserId requester = UserId.New();
        UserId oldest = UserId.New();
        UserId middle = UserId.New();
        UserId newest = UserId.New();

        Conversation first = CreateDirect(requester, oldest, UtcStamp);
        Conversation second = CreateDirect(requester, middle, UtcStamp.AddMinutes(1));
        Conversation third = CreateDirect(requester, newest, UtcStamp.AddMinutes(2));
        db.Conversations.AddRange(first, second, third);
        await db.SaveChangesAsync();

        var lookup = new FakeChatUserLookup();
        lookup.Add(oldest, "Oldest Peer");
        lookup.Add(middle, "Middle Peer");
        lookup.Add(newest, "Newest Peer", "avatar-newest");
        var handler = new GetConversationsQueryHandler(db, lookup);

        PagedResult<ConversationListItemResponse> page = (await handler.Handle(
            new GetConversationsQuery(requester, Page: 0, PageSize: 0), default)).Value;

        Assert.Equal(1, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(3, page.TotalPages);

        ConversationListItemResponse item = Assert.Single(page.Items);
        Assert.Equal(third.Id.Value, item.Id);
        Assert.Equal(newest.Value, item.Participant.UserId);
        Assert.Equal("Newest Peer", item.Participant.DisplayName);
        Assert.Equal("avatar-newest", item.Participant.AvatarUrl);
        Assert.Null(item.LastMessage); // STUB: messages are Lane B.
        Assert.Equal(0, item.UnreadCount); // STUB: unread is Lane B.
    }

    [Fact]
    public async Task Hide_UnknownAndForeignConversationsFailWithTheSamePrivacyCode()
    {
        await using ChatDbContext db = CreateDb();
        UserId owner = UserId.New();
        Conversation conversation = CreateDirect(owner, UserId.New(), UtcStamp);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new HideConversationCommandHandler(db);

        Result unknown = await handler.Handle(
            new HideConversationCommand(owner, ConversationId.New()), default);
        Result foreign = await handler.Handle(
            new HideConversationCommand(UserId.New(), conversation.Id), default);

        Assert.True(unknown.IsFailure);
        Assert.True(foreign.IsFailure);
        Assert.Equal(ConversationErrors.NotFound.Code, unknown.Error.Code);
        Assert.Equal(unknown.Error.Code, foreign.Error.Code);
        Assert.False((await db.Conversations.AsNoTracking().SingleAsync())
            .IsHiddenFor(owner));
    }

    [Fact]
    public async Task Hide_IsIdempotentForTheDeleterAndKeepsTheOtherPartyVisible()
    {
        await using ChatDbContext db = CreateDb();
        UserId deleter = UserId.New();
        UserId other = UserId.New();
        Conversation conversation = CreateDirect(deleter, other, UtcStamp);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new HideConversationCommandHandler(db);

        Assert.True((await handler.Handle(
            new HideConversationCommand(deleter, conversation.Id), default)).IsSuccess);
        Assert.True((await handler.Handle(
            new HideConversationCommand(deleter, conversation.Id), default)).IsSuccess);

        Conversation stored = await db.Conversations.AsNoTracking().SingleAsync();
        Assert.True(stored.IsHiddenFor(deleter));
        Assert.False(stored.IsHiddenFor(other));

        var listHandler = new GetConversationsQueryHandler(db, new FakeChatUserLookup());
        Assert.Equal(0, (await listHandler.Handle(
            new GetConversationsQuery(deleter), default)).Value.TotalCount);
        Assert.Equal(1, (await listHandler.Handle(
            new GetConversationsQuery(other), default)).Value.TotalCount);
    }

    [Fact]
    public async Task EligibilityChecker_AllowsRealRecipientsAndDeniesEverythingElseSilently()
    {
        UserId opener = UserId.New();
        UserId recipient = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(recipient, "Recipient");
        var checker = new ApiChatEligibilityChecker(lookup);

        Assert.True(await checker.CanOpenAsync(opener, recipient, null));
        Assert.True(await checker.CanOpenAsync(opener, recipient, BookingId.New()));

        Assert.False(await checker.CanOpenAsync(opener, opener, null));
        Assert.False(await checker.CanOpenAsync(UserId.Empty, recipient, null));
        Assert.False(await checker.CanOpenAsync(opener, UserId.Empty, null));
        Assert.False(await checker.CanOpenAsync(opener, UserId.New(), null));
        Assert.False(await checker.CanOpenAsync(
            opener, recipient, new BookingId(Guid.Empty)));
    }

    // ------------------------------------------------------------------
    // Endpoint 1 / 2 / 10 — controller contract and JWT dispatch.
    // ------------------------------------------------------------------

    [Fact]
    public void ChatController_ExposesEndpoints1210UnderAuthorizeOnTheChatRoute()
    {
        Assert.Equal(
            "api/v1/chat",
            Assert.Single(typeof(ChatController).GetCustomAttributes<RouteAttribute>()).Template);

        var authorize = Assert.Single(typeof(ChatController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Null(authorize.Policy);

        System.Reflection.MethodInfo list =
            typeof(ChatController).GetMethod(nameof(ChatController.GetConversations))!;
        System.Reflection.MethodInfo open =
            typeof(ChatController).GetMethod(nameof(ChatController.OpenConversation))!;
        System.Reflection.MethodInfo hide =
            typeof(ChatController).GetMethod(nameof(ChatController.HideConversation))!;

        Assert.Equal("conversations", list.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("conversations", open.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(
            "conversations/{conversationId:guid}",
            hide.GetCustomAttribute<HttpDeleteAttribute>()!.Template);

        Assert.Contains(list.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK);
        Assert.Contains(open.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK);
        Assert.Contains(hide.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status204NoContent);
        Assert.Contains(hide.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound);

        // Paging defaults are part of the frozen contract (1 / 20).
        Assert.Equal(1, list.GetParameters().Single(parameter => parameter.Name == "page").DefaultValue);
        Assert.Equal(20, list.GetParameters().Single(parameter => parameter.Name == "pageSize").DefaultValue);

        // No conversation-detail route is invented in Lane A.
        Assert.DoesNotContain(typeof(ChatController).GetMethods(),
            method => method.GetCustomAttribute<HttpGetAttribute>()?.Template == "{conversationId:guid}");
    }

    [Fact]
    public async Task GetConversations_RequiresAJwtActorAndDispatchesRequesterWithPaging()
    {
        var sender = new CapturingSender(
            Result<PagedResult<ConversationListItemResponse>>.Success(
                new([], 1, 20, 0, 0)));
        ChatController anonymous = CreateController(sender);

        Assert.IsType<UnauthorizedResult>(await anonymous.GetConversations());
        Assert.Null(sender.LastRequest);

        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);

        IActionResult result = await controller.GetConversations(
            page: 2, pageSize: 5, cancellationToken: default);

        Assert.IsType<OkObjectResult>(result);
        GetConversationsQuery query =
            Assert.IsType<GetConversationsQuery>(sender.LastRequest);
        Assert.Equal(actor, query.Requester);
        Assert.Equal(2, query.Page);
        Assert.Equal(5, query.PageSize);
    }

    [Fact]
    public async Task OpenConversation_DispatchesTheJwtActorRecipientAndOptionalBooking()
    {
        var sender = new CapturingSender(
            Result<OpenConversationResponse>.Success(
                new OpenConversationResponse(Guid.NewGuid(), Created: true)));
        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);
        UserId recipient = UserId.New();
        Guid bookingId = Guid.NewGuid();

        IActionResult created = await controller.OpenConversation(
            new OpenConversationRequest(recipient.Value, bookingId), default);

        Assert.IsType<OkObjectResult>(created);
        OpenConversationCommand command =
            Assert.IsType<OpenConversationCommand>(sender.LastRequest);
        Assert.Equal(actor, command.Opener);
        Assert.Equal(recipient, command.Recipient);
        Assert.Equal(new BookingId(bookingId), command.BookingId);

        await controller.OpenConversation(
            new OpenConversationRequest(recipient.Value, null), default);
        OpenConversationCommand direct =
            Assert.IsType<OpenConversationCommand>(sender.LastRequest);
        Assert.Null(direct.BookingId);
    }

    [Fact]
    public async Task HideConversation_Returns204OnSuccessAndAGenericPrivacy404OnFailure()
    {
        var sender = new CapturingSender(Result.Success());
        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);
        Guid conversationId = Guid.NewGuid();

        IActionResult hidden = await controller.HideConversation(conversationId, default);

        Assert.IsType<NoContentResult>(hidden);
        HideConversationCommand command =
            Assert.IsType<HideConversationCommand>(sender.LastRequest);
        Assert.Equal(actor, command.Requester);
        Assert.Equal(new ConversationId(conversationId), command.ConversationId);

        ChatController failing = CreateController(
            new CapturingSender(Result.Failure(ConversationErrors.NotFound)), actor);
        IActionResult denied = await failing.HideConversation(conversationId, default);

        ObjectResult objectResult = Assert.IsType<ObjectResult>(denied);
        Assert.Equal(StatusCodes.Status404NotFound, objectResult.StatusCode);
        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(
            ConversationErrors.NotFound.Code,
            problemDetails.Extensions["code"]);
        // The detail is the generic 404 text — no reason is ever revealed.
        Assert.Equal(
            "The requested resource was not found.",
            problemDetails.Detail);
    }

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Conversation CreateDirect(
        UserId participantA,
        UserId participantB,
        DateTime createdOnUtc) =>
        Conversation.Create(
            participantA,
            participantB,
            ConversationScope.Direct,
            null,
            createdOnUtc);

    private static ChatController CreateController(ISender sender, UserId? actor = null)
    {
        var controller = new ChatController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        if (actor.HasValue)
        {
            controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(
                new ClaimsIdentity(
                    [new Claim(JwtRegisteredClaimNames.Sub, actor.Value.Value.ToString())],
                    "test"));
        }

        return controller;
    }

    private sealed class FakeChatUserLookup : IChatUserLookup
    {
        private readonly Dictionary<UserId, UserProfile> _profiles = new();

        public void Add(UserId userId, string displayName, string? avatarUrl = null) =>
            _profiles[userId] = new UserProfile(userId, displayName, avatarUrl);

        public Task<UserProfile?> GetProfileAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _profiles.TryGetValue(userId, out UserProfile? profile)
                    ? profile
                    : null);
    }

    private sealed class CapturingSender(object response) : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
