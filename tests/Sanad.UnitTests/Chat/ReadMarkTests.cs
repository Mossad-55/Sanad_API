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
using Sanad.Modules.Chat.Domain.Conversations;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Endpoint 7 (PUT …/{id}/read) over an EF InMemory ChatDbContext. InMemory
/// does not enforce the unique (ConversationId, UserId) index, so upsert
/// idempotency is proven at handler level; the index in the rescaffolded
/// InitialChat migration is the database backstop. Lane A has no Message
/// table, so message existence is deliberately NOT asserted anywhere here:
/// the seam documented in <see cref="ConversationReadState"/> is that Lane A
/// records the mark verbatim and Lane B interprets it.
/// </summary>
public sealed class ReadMarkTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 5, 12, 0, 0), DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Domain — ConversationReadState.Create validation.
    // ------------------------------------------------------------------

    [Fact]
    public void Create_StoresTheMarkAndRejectsEmptyIdsAndNonUtcStamps()
    {
        Conversation conversation = CreateDirect(UserId.New(), UserId.New());
        UserId reader = UserId.New();
        Guid messageId = Guid.NewGuid();

        ConversationReadState state = ConversationReadState.Create(
            conversation.Id, reader, messageId, UtcStamp);

        Assert.Equal(conversation.Id, state.ConversationId);
        Assert.Equal(reader, state.UserId);
        Assert.Equal(messageId, state.LastReadMessageId);
        Assert.Equal(UtcStamp, state.ReadOnUtc);
        Assert.Equal(DateTimeKind.Utc, state.ReadOnUtc.Kind);

        Assert.Throws<DomainException>(() => ConversationReadState.Create(
            ConversationId.Empty, reader, messageId, UtcStamp));
        Assert.Throws<DomainException>(() => ConversationReadState.Create(
            conversation.Id, UserId.Empty, messageId, UtcStamp));
        Assert.Throws<DomainException>(() => ConversationReadState.Create(
            conversation.Id, reader, Guid.Empty, UtcStamp));
        Assert.Throws<DomainException>(() => ConversationReadState.Create(
            conversation.Id,
            reader,
            messageId,
            new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Local)));

        // The re-mark path validates the same way.
        Assert.Throws<DomainException>(() => state.Mark(Guid.Empty, UtcStamp));
        Assert.Throws<DomainException>(() => state.Mark(
            messageId, new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Unspecified)));
        Assert.Equal(messageId, state.LastReadMessageId);
    }

    // ------------------------------------------------------------------
    // Endpoint 7 — handler: create, idempotency, upsert, isolation.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Mark_CreatesTheReadStateForTheParticipant()
    {
        await using ChatDbContext db = CreateDb();
        UserId reader = UserId.New();
        Conversation conversation = CreateDirect(reader, UserId.New());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new MarkConversationReadCommandHandler(db);
        Guid messageId = Guid.NewGuid();

        var result = await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, messageId),
            default);

        Assert.True(result.IsSuccess);

        ConversationReadState stored = await db.ReadStates.SingleAsync();
        Assert.Equal(conversation.Id, stored.ConversationId);
        Assert.Equal(reader, stored.UserId);
        Assert.Equal(messageId, stored.LastReadMessageId);
        Assert.Equal(DateTimeKind.Utc, stored.ReadOnUtc.Kind);

        // A read mark is not activity: the conversation's last-activity
        // stamp belongs to the message flow (Lane B) and stays put.
        Conversation reloaded = await db.Conversations.AsNoTracking()
            .SingleAsync(item => item.Id == conversation.Id);
        Assert.Equal(conversation.LastActivityOnUtc, reloaded.LastActivityOnUtc);
    }

    [Fact]
    public async Task Mark_RepeatingTheSameCallKeepsOneRowAndStillSucceeds()
    {
        await using ChatDbContext db = CreateDb();
        UserId reader = UserId.New();
        Conversation conversation = CreateDirect(reader, UserId.New());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new MarkConversationReadCommandHandler(db);
        Guid messageId = Guid.NewGuid();
        var mark = new MarkConversationReadCommand(reader, conversation.Id, messageId);

        Assert.True((await handler.Handle(mark, default)).IsSuccess);
        DateTime firstStamp = (await db.ReadStates.SingleAsync()).ReadOnUtc;

        Assert.True((await handler.Handle(mark, default)).IsSuccess);
        Assert.True((await handler.Handle(mark, default)).IsSuccess);

        ConversationReadState stored = await db.ReadStates.SingleAsync();
        Assert.Equal(messageId, stored.LastReadMessageId);
        // A refreshed (never regressed) stamp is allowed on a re-mark.
        Assert.True(stored.ReadOnUtc >= firstStamp);
    }

    [Fact]
    public async Task Mark_ADifferentMessageIdUpdatesTheExistingRowInsteadOfAddingOne()
    {
        await using ChatDbContext db = CreateDb();
        UserId reader = UserId.New();
        Conversation conversation = CreateDirect(reader, UserId.New());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new MarkConversationReadCommandHandler(db);
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();

        Assert.True((await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, first),
            default)).IsSuccess);
        Assert.True((await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, second),
            default)).IsSuccess);

        ConversationReadState stored = await db.ReadStates.SingleAsync();
        Assert.Equal(second, stored.LastReadMessageId);
        Assert.Equal(1, await db.ReadStates.CountAsync());
    }

    [Fact]
    public async Task Mark_KeepsOneRowPerUserAndNeverTouchesTheOtherParticipantsMark()
    {
        await using ChatDbContext db = CreateDb();
        UserId readerA = UserId.New();
        UserId readerB = UserId.New();
        Conversation conversation = CreateDirect(readerA, readerB);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new MarkConversationReadCommandHandler(db);
        Guid fromA = Guid.NewGuid();
        Guid fromB = Guid.NewGuid();

        Assert.True((await handler.Handle(
            new MarkConversationReadCommand(readerA, conversation.Id, fromA),
            default)).IsSuccess);
        Assert.True((await handler.Handle(
            new MarkConversationReadCommand(readerB, conversation.Id, fromB),
            default)).IsSuccess);

        Assert.Equal(2, await db.ReadStates.CountAsync());

        ConversationReadState stateA = await db.ReadStates.SingleAsync(
            item => item.UserId == readerA);
        ConversationReadState stateB = await db.ReadStates.SingleAsync(
            item => item.UserId == readerB);

        Assert.Equal(fromA, stateA.LastReadMessageId);
        Assert.Equal(fromB, stateB.LastReadMessageId);
        Assert.Equal(conversation.Id, stateA.ConversationId);
        Assert.Equal(conversation.Id, stateB.ConversationId);
    }

    // ------------------------------------------------------------------
    // Endpoint 7 — privacy gate and payload validation.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Mark_UnknownAndForeignConversationsFailWithTheSamePrivacyCode()
    {
        await using ChatDbContext db = CreateDb();
        UserId participant = UserId.New();
        Conversation conversation = CreateDirect(participant, UserId.New());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new MarkConversationReadCommandHandler(db);

        Result unknown = await handler.Handle(
            new MarkConversationReadCommand(
                participant, ConversationId.New(), Guid.NewGuid()),
            default);
        Result foreign = await handler.Handle(
            new MarkConversationReadCommand(
                UserId.New(), conversation.Id, Guid.NewGuid()),
            default);

        Assert.True(unknown.IsFailure);
        Assert.True(foreign.IsFailure);
        Assert.Equal(ConversationErrors.NotFound.Code, unknown.Error.Code);
        Assert.Equal(unknown.Error.Code, foreign.Error.Code);
        Assert.Equal(0, await db.ReadStates.CountAsync());
    }

    [Fact]
    public async Task Mark_EmptyMessageIdFailsWithTheInvalidCodeAndPersistsNothing()
    {
        await using ChatDbContext db = CreateDb();
        UserId reader = UserId.New();
        Conversation conversation = CreateDirect(reader, UserId.New());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new MarkConversationReadCommandHandler(db);

        var result = await handler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, Guid.Empty),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(ConversationReadErrors.Invalid.Code, result.Error.Code);
        Assert.Equal(0, await db.ReadStates.CountAsync());
    }

    [Fact]
    public async Task Mark_IsNeverReachableForANonParticipantEvenWithAMalformedBody()
    {
        await using ChatDbContext db = CreateDb();
        UserId participant = UserId.New();
        Conversation conversation = CreateDirect(participant, UserId.New());
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();
        var handler = new MarkConversationReadCommandHandler(db);

        // The privacy gate runs before payload validation, so a stranger
        // always learns the same thing: nothing.
        Result stranger = await handler.Handle(
            new MarkConversationReadCommand(
                UserId.New(), conversation.Id, Guid.Empty),
            default);

        Assert.True(stranger.IsFailure);
        Assert.Equal(ConversationErrors.NotFound.Code, stranger.Error.Code);
        Assert.Equal(0, await db.ReadStates.CountAsync());
    }

    // ------------------------------------------------------------------
    // Endpoint 7 — independence from hide / reopen / visibility.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Mark_SurvivesHideAndReopenAndNeitherSideCreatesOrClearsAMark()
    {
        await using ChatDbContext db = CreateDb();
        UserId reader = UserId.New();
        UserId peer = UserId.New();
        Conversation conversation = CreateDirect(reader, peer);
        db.Conversations.Add(conversation);
        await db.SaveChangesAsync();

        var lookup = new FakeChatUserLookup();
        lookup.Add(peer, "Peer");
        var openHandler = new OpenConversationCommandHandler(
            db, new ApiChatEligibilityChecker(lookup), new FakeChatBookingGateway());
        var hideHandler = new HideConversationCommandHandler(db);
        var markHandler = new MarkConversationReadCommandHandler(db);
        Guid messageId = Guid.NewGuid();

        Assert.True((await markHandler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, messageId),
            default)).IsSuccess);
        DateTime lastActivity = conversation.LastActivityOnUtc;

        Assert.True((await hideHandler.Handle(
            new HideConversationCommand(reader, conversation.Id), default)).IsSuccess);

        // Hiding neither creates nor clears a mark, and it is not activity.
        Assert.Equal(1, await db.ReadStates.CountAsync());
        Conversation afterHide = await db.Conversations.SingleAsync();
        Assert.True(afterHide.IsHiddenFor(reader));
        Assert.Equal(lastActivity, afterHide.LastActivityOnUtc);

        // Reopen (unhide) restores visibility for that participant only.
        OpenConversationResponse reopened = (await openHandler.Handle(
            new OpenConversationCommand(reader, peer, null), default)).Value;
        Assert.Equal(conversation.Id.Value, reopened.ConversationId);

        Conversation afterOpen = await db.Conversations.AsNoTracking()
            .SingleAsync(item => item.Id == conversation.Id);
        Assert.False(afterOpen.IsHiddenFor(reader));
        Assert.False(afterOpen.IsHiddenFor(peer));
        Assert.Equal(lastActivity, afterOpen.LastActivityOnUtc);

        ConversationReadState stored = await db.ReadStates.SingleAsync();
        Assert.Equal(messageId, stored.LastReadMessageId);
        Assert.Equal(reader, stored.UserId);

        // Marking again after the reopen is still the same single row.
        Guid later = Guid.NewGuid();
        Assert.True((await markHandler.Handle(
            new MarkConversationReadCommand(reader, conversation.Id, later),
            default)).IsSuccess);
        Assert.Equal(1, await db.ReadStates.CountAsync());
        Assert.Equal(later, (await db.ReadStates.SingleAsync()).LastReadMessageId);
    }

    // ------------------------------------------------------------------
    // Endpoint 7 — controller contract and JWT dispatch.
    // ------------------------------------------------------------------

    [Fact]
    public void ChatController_ExposesEndpoint7UnderAuthorizeOnTheChatRoute()
    {
        Assert.Equal(
            "api/v1/chat",
            Assert.Single(typeof(ChatController).GetCustomAttributes<RouteAttribute>()).Template);

        var authorize = Assert.Single(typeof(ChatController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Null(authorize.Policy);

        System.Reflection.MethodInfo mark =
            typeof(ChatController).GetMethod(nameof(ChatController.MarkConversationRead))!;

        Assert.Equal(
            "conversations/{conversationId:guid}/read",
            mark.GetCustomAttribute<HttpPostAttribute>()!.Template);

        Assert.Contains(mark.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status204NoContent);
        Assert.Contains(mark.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Contains(mark.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task MarkConversationRead_RequiresAJwtDispatchesTheCommandAndAnswers204()
    {
        var sender = new CapturingSender(Result.Success());

        Assert.IsType<UnauthorizedResult>(
            await CreateController(sender).MarkConversationRead(
                Guid.NewGuid(), new ChatMarkReadRequest(Guid.NewGuid()), default));
        Assert.Null(sender.LastRequest);

        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);
        Guid conversationId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();

        IActionResult ok = await controller.MarkConversationRead(
            conversationId, new ChatMarkReadRequest(messageId), default);

        Assert.IsType<NoContentResult>(ok);
        MarkConversationReadCommand command =
            Assert.IsType<MarkConversationReadCommand>(sender.LastRequest);
        Assert.Equal(actor, command.Requester);
        Assert.Equal(new ConversationId(conversationId), command.ConversationId);
        Assert.Equal(messageId, command.LastReadMessageId);
    }

    [Fact]
    public async Task MarkConversationRead_SurfacesInvalidAs400AndNotFoundAsAGenericPrivacy404()
    {
        UserId actor = UserId.New();
        Guid conversationId = Guid.NewGuid();
        Guid messageId = Guid.NewGuid();

        ChatController invalid = CreateController(
            new CapturingSender(Result.Failure(ConversationReadErrors.Invalid)), actor);
        IActionResult badRequest = await invalid.MarkConversationRead(
            conversationId, new ChatMarkReadRequest(messageId), default);

        ObjectResult badRequestObject = Assert.IsType<ObjectResult>(badRequest);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestObject.StatusCode);
        var invalidDetails = Assert.IsType<ProblemDetails>(badRequestObject.Value);
        Assert.Equal(
            ConversationReadErrors.Invalid.Code,
            invalidDetails.Extensions["code"]);

        ChatController unknown = CreateController(
            new CapturingSender(Result.Failure(ConversationErrors.NotFound)), actor);
        IActionResult notFound = await unknown.MarkConversationRead(
            conversationId, new ChatMarkReadRequest(messageId), default);

        ObjectResult notFoundObject = Assert.IsType<ObjectResult>(notFound);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundObject.StatusCode);
        var unknownDetails = Assert.IsType<ProblemDetails>(notFoundObject.Value);
        Assert.Equal(
            ConversationErrors.NotFound.Code,
            unknownDetails.Extensions["code"]);
        // Generic detail — the caller never learns whether the id exists.
        Assert.Equal(
            "The requested resource was not found.",
            unknownDetails.Detail);
    }

    // ------------------------------------------------------------------
    // Harness.
    // ------------------------------------------------------------------

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static Conversation CreateDirect(UserId participantA, UserId participantB) =>
        Conversation.Create(
            participantA,
            participantB,
            ConversationScope.Direct,
            null,
            UtcStamp);

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

        public void Add(
            UserId userId,
            string displayName,
            string? avatarUrl = null,
            int? accountType = null) =>
            _profiles[userId] = new UserProfile(
                userId, displayName, avatarUrl, accountType);

        public Task<UserProfile?> GetProfileAsync(
            UserId userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _profiles.TryGetValue(userId, out UserProfile? profile)
                    ? profile
                    : null);
    }

    private sealed class FakeChatBookingGateway : IChatBookingGateway
    {
        public Task<UserId?> ResolveCounterpartAsync(
            BookingId bookingId,
            UserId openerUserId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult((UserId?)null);
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
