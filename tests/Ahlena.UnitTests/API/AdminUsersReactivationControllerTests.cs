using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Identity.Application.Users.Admin;
using Ahlena.Modules.Identity.Domain.Users;
using Ahlena.Modules.Identity.Infrastructure.Persistence;

namespace Ahlena.UnitTests.API;

public sealed class AdminUsersReactivationControllerTests
{
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Reactivate_ActivationNotReady_MapsTo409WithSafeDetailCodeAndTraceId()
    {
        var targetId = Guid.NewGuid();
        var controller = CreateController(
            new StubSender((request, _) => Task.FromResult<object>(
                request is ReactivateAdminUserCommand
                    ? Result<AdminUserStatusResponse>.Failure(AdminUserErrors.ActivationNotReady)
                    : throw new NotSupportedException())),
            Guid.NewGuid(),
            "reactivation-trace");

        IActionResult result = await controller.Reactivate(targetId, default);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal(StatusCodes.Status409Conflict, problem.Status);
        Assert.Equal("Conflict", problem.Title);
        Assert.Equal(
            "The user does not meet the activation requirements. Complete the required verification and account setup before reactivation.",
            problem.Detail);
        Assert.Equal("Identity.Admin.ActivationNotReady", problem.Extensions["code"]);
        Assert.Equal("reactivation-trace", problem.Extensions["traceId"]);
    }

    [Fact]
    public async Task Reactivate_Success_Returns200WithExistingResultShape()
    {
        var targetId = Guid.NewGuid();
        var expected = new AdminUserStatusResponse(targetId, UserStatus.Active, 0);
        var controller = CreateController(
            new StubSender((request, _) => Task.FromResult<object>(
                request is ReactivateAdminUserCommand
                    ? Result<AdminUserStatusResponse>.Success(expected)
                    : throw new NotSupportedException())),
            Guid.NewGuid(),
            "success-trace");

        IActionResult result = await controller.Reactivate(targetId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(StatusCodes.Status200OK, ok.StatusCode);
        Assert.Equal(expected, Assert.IsType<AdminUserStatusResponse>(ok.Value));
    }

    [Theory]
    [InlineData("Identity.Admin.NotSuspended", StatusCodes.Status409Conflict)]
    [InlineData("Identity.User.NotFound", StatusCodes.Status404NotFound)]
    [InlineData("Identity.Admin.SeededProtected", StatusCodes.Status403Forbidden)]
    public async Task Reactivate_OldFailures_KeepExistingMappingsWithoutNewDetail(string code, int status)
    {
        var controller = CreateController(
            new StubSender((request, _) => Task.FromResult<object>(
                request is ReactivateAdminUserCommand
                    ? Result<AdminUserStatusResponse>.Failure(new Error(code, "internal"))
                    : throw new NotSupportedException())),
            Guid.NewGuid(),
            "old-failure-trace");

        IActionResult result = await controller.Reactivate(Guid.NewGuid(), default);

        var mapped = Assert.IsType<ObjectResult>(result);
        Assert.Equal(status, mapped.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(mapped.Value);
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal("old-failure-trace", problem.Extensions["traceId"]);
        Assert.Null(problem.Detail);
    }

    [Fact]
    public async Task Reactivate_PendingAdminChain_Returns409AndKeepsSuspendedState()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User actor = AddVerifiedUser(db, AccountType.SuperAdmin, "مدير", "Admin", "chain.admin@example.test", "+201000002001");
        User target = AddPendingAdminWithPassword(db, "معلق", "Pending", "chain.pending@example.test", "+201000002002");
        await db.SaveChangesAsync();
        var setupNotices = new RecordingNoticePublisher();
        Assert.True((await new SuspendAdminUserCommandHandler(db, new FixedClock(Now), new NeverSeededProtection(), setupNotices)
            .Handle(new(actor.Id, target.Id, "Security review"), default)).IsSuccess);
        var handler = new ReactivateAdminUserCommandHandler(db, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), new RecordingNoticePublisher());
        var controller = CreateController(
            new DelegatingSender(handler),
            actor.Id.Value,
            "chain-trace");

        IActionResult result = await controller.Reactivate(target.Id.Value, default);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(conflict.Value);
        Assert.Equal("Identity.Admin.ActivationNotReady", problem.Extensions["code"]);
        Assert.Equal(
            "The user does not meet the activation requirements. Complete the required verification and account setup before reactivation.",
            problem.Detail);
        Assert.Equal("chain-trace", problem.Extensions["traceId"]);
        User reloaded = await db.Users.Include(x => x.Accounts).SingleAsync(x => x.Id == target.Id);
        Assert.Equal(UserStatus.Suspended, reloaded.Status);
        Assert.Equal("Security review", reloaded.StatusReason);
        Assert.Empty(await db.AdminUserActions.Where(x => x.TargetUserId == target.Id && x.Action == AdminUserActionType.Reactivated).ToListAsync());
    }

    [Fact]
    public async Task Reactivate_VerifiedChain_Returns200AndActivates()
    {
        await using IdentityDbContext db = CreateIdentityDb();
        User actor = AddVerifiedUser(db, AccountType.SuperAdmin, "مدير", "Admin", "chain2.admin@example.test", "+201000002011");
        User target = AddVerifiedUser(db, AccountType.SupportAdmin, "مستخدم", "User", "chain2.user@example.test", "+201000002012");
        await db.SaveChangesAsync();
        var setupNotices = new RecordingNoticePublisher();
        Assert.True((await new SuspendAdminUserCommandHandler(db, new FixedClock(Now), new NeverSeededProtection(), setupNotices)
            .Handle(new(actor.Id, target.Id, "Security review"), default)).IsSuccess);
        var handler = new ReactivateAdminUserCommandHandler(db, new FixedClock(Now.AddMinutes(1)), new NeverSeededProtection(), new RecordingNoticePublisher());
        var controller = CreateController(new DelegatingSender(handler), actor.Id.Value, "chain2-trace");

        IActionResult result = await controller.Reactivate(target.Id.Value, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = Assert.IsType<AdminUserStatusResponse>(ok.Value);
        Assert.Equal(UserStatus.Active, body.Status);
        Assert.Equal(UserStatus.Active, target.Status);
        Assert.Null(target.StatusReason);
    }

    private static AdminUsersController CreateController(ISender sender, Guid actorId, string traceId)
    {
        var controller = new AdminUsersController(sender);
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = traceId;
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, actorId.ToString())], "Test"));
        controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return controller;
    }

    private static IdentityDbContext CreateIdentityDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N") + "-identity")
            .Options;
        return new IdentityDbContext(options);
    }

    private static User AddVerifiedUser(IdentityDbContext db, AccountType type, string arabic, string english, string email, string phone)
    {
        User user = User.Create(FullName.Create(arabic), FullName.Create(english), Email.Create(email), PhoneNumber.Create(phone));
        user.AddAccount(type);
        user.SetInitialPasswordHash("test-hash", Now);
        user.VerifyEmail(Now);
        user.VerifyPhone(Now);
        user.Activate(Now);
        db.Users.Add(user);
        return user;
    }

    private static User AddPendingAdminWithPassword(IdentityDbContext db, string arabic, string english, string email, string phone)
    {
        User user = User.Create(FullName.Create(arabic), FullName.Create(english), Email.Create(email), PhoneNumber.Create(phone));
        user.AddAccount(AccountType.SupportAdmin);
        user.SetInitialPasswordHash("test-hash", Now);
        db.Users.Add(user);
        return user;
    }

    private sealed class StubSender(Func<object, CancellationToken, Task<object>> dispatch) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            (TResponse)await dispatch(request, cancellationToken);

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class DelegatingSender(ReactivateAdminUserCommandHandler handler) : ISender
    {
        public async Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            if (request is ReactivateAdminUserCommand command)
                return (TResponse)(object)await handler.Handle(command, cancellationToken);
            throw new NotSupportedException();
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NeverSeededProtection : IAdminUserSeedProtection
    {
        public bool IsSeeded(User user) => false;
    }

    private sealed class RecordingNoticePublisher : IAdminUserStatusNoticePublisher
    {
        public List<AdminUserStatusNotice> Items { get; } = [];
        public Task PublishAsync(AdminUserStatusNotice notice, CancellationToken cancellationToken)
        {
            Items.Add(notice);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedClock(DateTime utcNow) : IDateTimeProvider
    {
        public DateTime UtcNow { get; } = utcNow;
    }
}
