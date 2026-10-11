using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Chat.Application.Blocks;
using Sanad.Modules.Chat.Application.Conversations;
using Sanad.Modules.Chat.Domain.Blocks;
using Sanad.Modules.Chat.Infrastructure.Persistence;

namespace Sanad.UnitTests.Chat;

/// <summary>
/// Endpoint 9 (block / unblock / list) over an EF InMemory ChatDbContext with
/// a faked <see cref="IChatUserLookup"/>. InMemory does not enforce unique
/// indexes, so block idempotency is proven at handler level rather than by
/// the database. There is no send path in Lane A: the generic-failure rule
/// (blocked sender receives the same generic failure as not-participant,
/// never a block reveal) is documented at the enforcement seam and proven by
/// the block state being queryable exactly the way Lane B will read it.
/// </summary>
public sealed class BlockTests
{
    private static DateTime UtcStamp =>
        DateTime.SpecifyKind(new DateTime(2026, 10, 3, 12, 0, 0), DateTimeKind.Utc);

    // ------------------------------------------------------------------
    // Endpoint 9 — block handler: create, idempotency, self, unknown.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Block_CreatesTheRowAndListsItForTheBlockerOnly()
    {
        await using ChatDbContext db = CreateDb();
        UserId blocker = UserId.New();
        UserId blocked = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(blocked, "Blocked");
        var handler = new BlockUserCommandHandler(db, lookup);

        var result = await handler.Handle(
            new BlockUserCommand(blocker, blocked),
            default);

        Assert.True(result.IsSuccess);

        Block stored = await db.Blocks.SingleAsync();
        Assert.Equal(blocker, stored.BlockerUserId);
        Assert.Equal(blocked, stored.BlockedUserId);

        // The list is the blocker's own; the blocked user's list is empty.
        var listHandler = new GetBlockedUsersQueryHandler(db);
        Guid[] mine = (await listHandler.Handle(
            new GetBlockedUsersQuery(blocker), default)).Value;
        Guid[] theirs = (await listHandler.Handle(
            new GetBlockedUsersQuery(blocked), default)).Value;

        Assert.Equal(new[] { blocked.Value }, mine);
        Assert.Empty(theirs);
    }

    [Fact]
    public async Task Block_IsIdempotentAndKeepsASingleRow()
    {
        await using ChatDbContext db = CreateDb();
        UserId blocker = UserId.New();
        UserId blocked = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(blocked, "Blocked");
        var handler = new BlockUserCommandHandler(db, lookup);
        var command = new BlockUserCommand(blocker, blocked);

        Assert.True((await handler.Handle(command, default)).IsSuccess);
        Assert.True((await handler.Handle(command, default)).IsSuccess);

        Assert.Equal(1, await db.Blocks.CountAsync());
    }

    [Fact]
    public async Task Block_SelfBlockFailsWithTheInvalidCodeAndPersistsNothing()
    {
        await using ChatDbContext db = CreateDb();
        UserId user = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(user, "Self");
        var handler = new BlockUserCommandHandler(db, lookup);

        var result = await handler.Handle(
            new BlockUserCommand(user, user),
            default);

        Assert.True(result.IsFailure);
        Assert.Equal(BlockErrors.Invalid.Code, result.Error.Code);
        Assert.Equal(0, await db.Blocks.CountAsync());
    }

    [Fact]
    public async Task Block_UnknownTargetFailsWithThePrivacyCodeAndPersistsNothing()
    {
        await using ChatDbContext db = CreateDb();
        var handler = new BlockUserCommandHandler(
            db, new FakeChatUserLookup());

        var result = await handler.Handle(
            new BlockUserCommand(UserId.New(), UserId.New()),
            default);

        // Same mechanism as an unknown conversation: the code exists for the
        // controller's privacy mapping and never confirms registration.
        Assert.True(result.IsFailure);
        Assert.Equal(BlockErrors.NotFound.Code, result.Error.Code);
        Assert.Equal(0, await db.Blocks.CountAsync());
    }

    // ------------------------------------------------------------------
    // Endpoint 9 — unblock handler: removal, restore, missing-row 204.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Unblock_RemovesTheRowAndRestoresReblocking()
    {
        await using ChatDbContext db = CreateDb();
        UserId blocker = UserId.New();
        UserId blocked = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(blocked, "Blocked");
        var blockHandler = new BlockUserCommandHandler(db, lookup);
        var unblockHandler = new UnblockUserCommandHandler(db);
        var block = new BlockUserCommand(blocker, blocked);

        Assert.True((await blockHandler.Handle(block, default)).IsSuccess);
        Assert.True((await unblockHandler.Handle(
            new UnblockUserCommand(blocker, blocked), default)).IsSuccess);

        Assert.Equal(0, await db.Blocks.CountAsync());

        // Unblock restores sending by removing the exact row Lane B checks;
        // re-blocking works and creates a fresh row.
        Assert.True((await blockHandler.Handle(block, default)).IsSuccess);
        Assert.Equal(1, await db.Blocks.CountAsync());
    }

    [Fact]
    public async Task Unblock_MissingRowStillSucceeds()
    {
        await using ChatDbContext db = CreateDb();

        var result = await new UnblockUserCommandHandler(db).Handle(
            new UnblockUserCommand(UserId.New(), UserId.New()),
            default);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, await db.Blocks.CountAsync());
    }

    // ------------------------------------------------------------------
    // Endpoint 9 — list handler: ordering and per-requester isolation.
    // ------------------------------------------------------------------

    [Fact]
    public async Task GetBlockedUsers_OrdersMostRecentFirstAndIsIsolatedPerRequester()
    {
        await using ChatDbContext db = CreateDb();
        UserId userA = UserId.New();
        UserId userB = UserId.New();
        UserId userC = UserId.New();

        // Seeded directly with distinct stamps so ordering is deterministic
        // (handler-created rows use DateTime.UtcNow).
        db.Blocks.AddRange(
            Block.Create(userA, userC, UtcStamp),
            Block.Create(userA, userB, UtcStamp.AddMinutes(1)),
            Block.Create(userB, userA, UtcStamp));
        await db.SaveChangesAsync();

        var handler = new GetBlockedUsersQueryHandler(db);

        Guid[] fromA = (await handler.Handle(
            new GetBlockedUsersQuery(userA), default)).Value;
        Guid[] fromB = (await handler.Handle(
            new GetBlockedUsersQuery(userB), default)).Value;

        // Most-recent first: B (newer) before C (older). userB's block of A
        // never leaks into userA's list and vice versa.
        Assert.Equal(new[] { userB.Value, userC.Value }, fromA);
        Assert.Equal(new[] { userA.Value }, fromB);
    }

    // ------------------------------------------------------------------
    // Lane B seam — generic-failure rule. Lane A has no send path, so the
    // rule is documented in BlockUserCommandHandler and here asserted as
    // state: the exact predicate Lane B will use (blocker/blocked pair in
    // Blocks) is queryable, and unblock flips it off. Lane B folds this
    // into the SAME generic privacy failure as not-participant
    // (Chat.Conversation.NotFound) — never a block-specific code.
    // ------------------------------------------------------------------

    [Fact]
    public async Task BlockStateIsQueryableForLaneBEnforcementAndUnblockTurnsItOff()
    {
        await using ChatDbContext db = CreateDb();
        UserId blocker = UserId.New();
        UserId blocked = UserId.New();
        var lookup = new FakeChatUserLookup();
        lookup.Add(blocked, "Blocked");
        var blockHandler = new BlockUserCommandHandler(db, lookup);
        var unblockHandler = new UnblockUserCommandHandler(db);

        Assert.True((await blockHandler.Handle(
            new BlockUserCommand(blocker, blocked), default)).IsSuccess);

        // The enforcement predicate: a row (blocker, blocked) denies sends
        // FROM the blocked side TO the blocker. Directional — the blocker
        // may still send.
        Assert.True(await db.Blocks.AnyAsync(item =>
            item.BlockerUserId == blocker && item.BlockedUserId == blocked));
        Assert.False(await db.Blocks.AnyAsync(item =>
            item.BlockerUserId == blocked && item.BlockedUserId == blocker));

        Assert.True((await unblockHandler.Handle(
            new UnblockUserCommand(blocker, blocked), default)).IsSuccess);

        Assert.False(await db.Blocks.AnyAsync(item =>
            item.BlockerUserId == blocker && item.BlockedUserId == blocked));
    }

    // ------------------------------------------------------------------
    // Endpoint 9 — controller contract and JWT dispatch.
    // ------------------------------------------------------------------

    [Fact]
    public void ChatController_ExposesEndpoint9UnderAuthorizeOnTheChatRoute()
    {
        Assert.Equal(
            "api/v1/chat",
            Assert.Single(typeof(ChatController).GetCustomAttributes<RouteAttribute>()).Template);

        var authorize = Assert.Single(typeof(ChatController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Null(authorize.Policy);

        System.Reflection.MethodInfo block =
            typeof(ChatController).GetMethod(nameof(ChatController.BlockUser))!;
        System.Reflection.MethodInfo unblock =
            typeof(ChatController).GetMethod(nameof(ChatController.UnblockUser))!;
        System.Reflection.MethodInfo list =
            typeof(ChatController).GetMethod(nameof(ChatController.GetBlockedUsers))!;

        Assert.Equal("blocks", block.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(
            "blocks/{userId:guid}",
            unblock.GetCustomAttribute<HttpDeleteAttribute>()!.Template);
        Assert.Equal("blocks", list.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.Contains(block.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status204NoContent);
        Assert.Contains(block.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status400BadRequest);
        Assert.Contains(block.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status404NotFound);
        Assert.Contains(unblock.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status204NoContent);
        Assert.Contains(list.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status200OK);
    }

    [Fact]
    public async Task BlockUser_RequiresAJwtDispatchesActorAndTargetAndAnswers204()
    {
        var sender = new CapturingSender(Result.Success());

        Assert.IsType<UnauthorizedResult>(
            await CreateController(sender).BlockUser(
                new BlockUserRequest(Guid.NewGuid()), default));
        Assert.Null(sender.LastRequest);

        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);
        Guid target = Guid.NewGuid();

        IActionResult created = await controller.BlockUser(
            new BlockUserRequest(target), default);

        Assert.IsType<NoContentResult>(created);
        BlockUserCommand command =
            Assert.IsType<BlockUserCommand>(sender.LastRequest);
        Assert.Equal(actor, command.Blocker);
        Assert.Equal(new UserId(target), command.Blocked);
    }

    [Fact]
    public async Task BlockUser_SurfacesInvalidAs400AndUnknownTargetAsAGenericPrivacy404()
    {
        UserId actor = UserId.New();
        Guid target = Guid.NewGuid();

        ChatController invalid = CreateController(
            new CapturingSender(Result.Failure(BlockErrors.Invalid)), actor);
        IActionResult badRequest = await invalid.BlockUser(
            new BlockUserRequest(target), default);

        ObjectResult badRequestObject = Assert.IsType<ObjectResult>(badRequest);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequestObject.StatusCode);
        var invalidDetails = Assert.IsType<ProblemDetails>(badRequestObject.Value);
        Assert.Equal(BlockErrors.Invalid.Code, invalidDetails.Extensions["code"]);

        ChatController unknown = CreateController(
            new CapturingSender(Result.Failure(BlockErrors.NotFound)), actor);
        IActionResult notFound = await unknown.BlockUser(
            new BlockUserRequest(target), default);

        ObjectResult notFoundObject = Assert.IsType<ObjectResult>(notFound);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundObject.StatusCode);
        var unknownDetails = Assert.IsType<ProblemDetails>(notFoundObject.Value);
        Assert.Equal(BlockErrors.NotFound.Code, unknownDetails.Extensions["code"]);
        // Generic detail — the caller never learns whether the id exists.
        Assert.Equal(
            "The requested resource was not found.",
            unknownDetails.Detail);
    }

    [Fact]
    public async Task UnblockUser_RequiresAJwtDispatchesActorAndTargetAndAnswers204()
    {
        var sender = new CapturingSender(Result.Success());

        Assert.IsType<UnauthorizedResult>(
            await CreateController(sender).UnblockUser(Guid.NewGuid(), default));
        Assert.Null(sender.LastRequest);

        UserId actor = UserId.New();
        ChatController controller = CreateController(sender, actor);
        Guid target = Guid.NewGuid();

        IActionResult removed = await controller.UnblockUser(target, default);

        Assert.IsType<NoContentResult>(removed);
        UnblockUserCommand command =
            Assert.IsType<UnblockUserCommand>(sender.LastRequest);
        Assert.Equal(actor, command.Blocker);
        Assert.Equal(new UserId(target), command.Blocked);
    }

    [Fact]
    public async Task GetBlockedUsers_RequiresAJwtDispatchesTheRequesterAndReturnsIds()
    {
        UserId actor = UserId.New();
        Guid blockedId = Guid.NewGuid();
        var sender = new CapturingSender(
            Result<Guid[]>.Success(new[] { blockedId }));

        Assert.IsType<UnauthorizedResult>(
            await CreateController(sender).GetBlockedUsers(default));
        Assert.Null(sender.LastRequest);

        ChatController controller = CreateController(sender, actor);

        IActionResult ok = await controller.GetBlockedUsers(default);

        OkObjectResult okObject = Assert.IsType<OkObjectResult>(ok);
        Assert.Equal(
            new[] { blockedId },
            Assert.IsType<Guid[]>(okObject.Value));

        GetBlockedUsersQuery query =
            Assert.IsType<GetBlockedUsersQuery>(sender.LastRequest);
        Assert.Equal(actor, query.Requester);
    }

    // ------------------------------------------------------------------
    // Harness.
    // ------------------------------------------------------------------

    private static ChatDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

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
