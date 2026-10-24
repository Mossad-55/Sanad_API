using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.BuildingBlocks.Domain.ValueObjects;
using Sanad.Modules.Community.Application.Comments;
using Sanad.Modules.Community.Application.Posts;
using Sanad.Modules.Community.Domain.Comments;
using Sanad.Modules.Community.Domain.Posts;
using Sanad.Modules.Community.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Abstractions.Security;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// FUR post management wire contract (Owner addition, locked 2026-10-23):
/// the app PUT/DELETE routes, the CommunityModeration Admin delete route, the
/// claim-derived acting author (no forged-id bypass), the privacy 404 / 409 /
/// 204 mappings and the absence of any Admin edit/impersonation path. In-process
/// real-host HTTP binding/authorization checks are authored below; the Mastermind
/// runtime slot executes them with the rest of the suite.
/// </summary>
public sealed class CommunityPostManagementApiTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public CommunityPostManagementApiTests(PortalApiMetadataHost host) => _host = host;

    // ------------------------------------------------- routes and policies

    [Fact]
    public void App_controller_exposes_the_locked_edit_and_delete_routes()
    {
        HttpMethodAttribute update = typeof(CommunityPostsController)
            .GetMethod(nameof(CommunityPostsController.UpdatePost))!
            .GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.Equal("PUT", update.HttpMethods.Single());
        Assert.Equal("posts/{postId:guid}", update.Template);

        HttpMethodAttribute delete = typeof(CommunityPostsController)
            .GetMethod(nameof(CommunityPostsController.DeletePost))!
            .GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.True(delete.HttpMethods.SequenceEqual(["DELETE"]));
        Assert.Equal("posts/{postId:guid}", delete.Template);
    }

    [Fact]
    public void Admin_controller_exposes_delete_only_under_the_moderation_policy()
    {
        var authorize = typeof(AdminCommunityPostsController).GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(authorize);
        Assert.Equal(Sanad.API.Authorization.AuthorizationPolicies.CommunityModeration, authorize!.Policy);

        HttpMethodAttribute delete = typeof(AdminCommunityPostsController)
            .GetMethod(nameof(AdminCommunityPostsController.Delete))!
            .GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.Equal("DELETE", delete.HttpMethods.Single());
        Assert.Equal("{postId:guid}", delete.Template);

        // Deletion is the only admin post write: no edit/impersonation actions
        // exist on the moderation controller at all.
        var editAttributes = typeof(AdminCommunityPostsController).GetMethods()
            .SelectMany(method => method.GetCustomAttributes<HttpMethodAttribute>())
            .Where(attribute => attribute.HttpMethods
                .Any(httpMethod => httpMethod is "PUT" or "PATCH"));
        Assert.Empty(editAttributes);
    }

    // ------------------------------------------------------- app behavior

    [Fact]
    public async Task Update_applies_the_claim_actor_and_returns_the_fresh_post()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        CommunityPostsController controller = CreatePostsController(db, author);
        var result = await controller.UpdatePost(
            post.Id.Value,
            new UpdatePostCommand(
                post.Id.Value,
                ActingAuthorId: UserId.New().Value, // a body value is irrelevant
                "عنوان جديد", "New Title", "محتوى جديد", "New content",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<CommunityPostResponse>(ok.Value);
        Assert.Equal("New Title", response.TitleEnglish);

        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(PostStatus.PendingReview, stored.Status); // moderation preserved
        Assert.Equal("New content", stored.ContentEnglish);
    }

    [Fact]
    public async Task Update_from_a_foreign_claim_is_a_privacy_404_and_mutates_nothing()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        CommunityPostsController controller = CreatePostsController(db, UserId.New());
        var result = await controller.UpdatePost(
            post.Id.Value,
            new UpdatePostCommand(
                post.Id.Value, UserId.New().Value,
                "هجوم", "Hacked", "هجوم", "Hacked",
                ImageUrl: null, IsAnonymous: true, CategoryId: null),
            default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal("Community.PostNotFound", details.Extensions["code"]?.ToString());

        Post stored = await db.Posts.SingleAsync();
        Assert.Equal("Title", stored.TitleEnglish);
        Assert.Equal(PostStatus.Published, stored.Status);
    }

    [Fact]
    public async Task Own_delete_is_204_then_the_repeat_is_404()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        CommunityPostsController controller = CreatePostsController(db, author);
        Assert.IsType<NoContentResult>(await controller.DeletePost(post.Id.Value, default));

        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(PostStatus.Deleted, stored.Status);
        Assert.Equal(author, stored.DeletedBy);

        // The second delete answers the same privacy not-found.
        var repeat = await controller.DeletePost(post.Id.Value, default);
        var problem = Assert.IsType<ObjectResult>(repeat);
        Assert.Equal(StatusCodes.Status404NotFound, problem.StatusCode);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal("Community.PostNotFound", details.Extensions["code"]?.ToString());
    }

    [Fact]
    public async Task Foreign_author_and_unknown_post_delete_are_privacy_404()
    {
        await using var db = CreateDb();
        Post post = CreatePublishedPost(UserId.New());
        db.Posts.Add(post);
        await db.SaveChangesAsync();

        CommunityPostsController controller = CreatePostsController(db, UserId.New());

        var foreign = await controller.DeletePost(post.Id.Value, default);
        Assert.Equal(StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(foreign).StatusCode);
        Assert.Equal(PostStatus.Published, (await db.Posts.SingleAsync()).Status);

        var unknown = await controller.DeletePost(Guid.NewGuid(), default);
        Assert.Equal(StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(unknown).StatusCode);
    }

    // -------------------------------------------------------- admin behavior

    [Fact]
    public async Task Admin_delete_removes_any_authors_post_with_moderator_audit()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        var moderator = UserId.New();

        AdminCommunityPostsController controller = CreateAdminController(db, moderator);

        Assert.IsType<NoContentResult>(await controller.Delete(post.Id.Value, default));

        Post stored = await db.Posts.SingleAsync();
        Assert.Equal(PostStatus.Deleted, stored.Status);
        Assert.Equal(moderator, stored.DeletedBy);
        Assert.NotEqual(author, stored.DeletedBy);
    }

    [Fact]
    public async Task Admin_delete_of_an_unknown_or_deleted_post_is_404()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        AdminCommunityPostsController controller = CreateAdminController(db, UserId.New());

        var unknown = await controller.Delete(Guid.NewGuid(), default);
        Assert.Equal(StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(unknown).StatusCode);

        post.Delete(author, DateTime.UtcNow);
        await db.SaveChangesAsync();

        var alreadyDeleted = await controller.Delete(post.Id.Value, default);
        Assert.Equal(StatusCodes.Status404NotFound,
            Assert.IsType<ObjectResult>(alreadyDeleted).StatusCode);
        // The author's own deletion audit remains (never overwritten).
        Assert.Equal(author, (await db.Posts.SingleAsync()).DeletedBy);
    }

    // ---------------------------------------------------------- race guard

    [Fact]
    public async Task Stale_author_edit_after_an_admin_delete_conflicts_and_the_deletion_survives()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CommunityDbContext>().UseSqlite(connection).Options;
        await using var authorContext = new CommunityDbContext(options);
        await authorContext.Database.EnsureCreatedAsync();

        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        authorContext.Posts.Add(post);
        await authorContext.SaveChangesAsync(); // stays tracked, goes stale

        await using var adminContext = new CommunityDbContext(options);
        AdminCommunityPostsController admin = CreateAdminController(adminContext, UserId.New());
        Assert.IsType<NoContentResult>(await admin.Delete(post.Id.Value, default));

        CommunityPostsController authorController = CreatePostsController(authorContext, author);
        var staleEdit = await authorController.UpdatePost(
            post.Id.Value,
            new UpdatePostCommand(
                post.Id.Value, author.Value,
                "أ", "Zombie", "ب", "Zombie body",
                ImageUrl: null, IsAnonymous: false, CategoryId: null),
            default);

        // The stale lifecycle write conflicts (409) instead of resurrecting.
        var conflict = Assert.IsType<ObjectResult>(staleEdit);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);

        await using var fresh = new CommunityDbContext(options);
        Assert.Equal(PostStatus.Deleted, (await fresh.Posts.AsNoTracking().SingleAsync()).Status);
    }

    // --------------------------------------- reactions stop after deletion

    [Fact]
    public async Task Reaction_endpoints_stop_after_deletion_with_wire_privacy_404s()
    {
        await using var db = CreateDb();
        var author = UserId.New();
        Post post = CreatePublishedPost(author);
        db.Posts.Add(post);
        Comment comment = Comment.Create(
            post.Id, author, "تعليق", "Comment", DateTime.UtcNow);
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        // A like retained before deletion.
        var liker = UserId.New();
        var like = await new LikePostCommandHandler(db).Handle(
            new LikePostCommand { PostId = post.Id.Value, UserId = liker.Value },
            default);
        Assert.True(like.Value);

        await new DeletePostCommandHandler(db).Handle(
            new DeletePostCommand(post.Id.Value, author.Value), default);

        CommunityPostsController controller = CreatePostsController(db, UserId.New());

        var likeCall = await controller.LikePost(post.Id.Value, default);
        var likeProblem = Assert.IsType<ObjectResult>(likeCall);
        Assert.Equal(StatusCodes.Status404NotFound, likeProblem.StatusCode);
        Assert.Equal("Community.PostNotFound",
            Assert.IsType<ProblemDetails>(likeProblem.Value).Extensions["code"]?.ToString());

        var favorite = await controller.FavoritePost(post.Id.Value, default);
        var favoriteProblem = Assert.IsType<ObjectResult>(favorite);
        Assert.Equal(StatusCodes.Status404NotFound, favoriteProblem.StatusCode);
        Assert.Equal("Community.PostNotFound",
            Assert.IsType<ProblemDetails>(favoriteProblem.Value).Extensions["code"]?.ToString());

        var unlike = await controller.UnlikePost(post.Id.Value, default);
        var unlikeProblem = Assert.IsType<ObjectResult>(unlike);
        Assert.Equal(StatusCodes.Status404NotFound, unlikeProblem.StatusCode);
        // The pre-deletion like stays retained after the rejected undo.
        Assert.Single(await db.Interactions.AsNoTracking().ToListAsync());

        CommentsController comments = CreateCommentsController(db, liker);
        Assert.IsType<NotFoundResult>(await comments.LikeComment(comment.Id.Value, default));
    }

    // --------------------- HTTP binding and authorization (real host)
    //
    // The approved testing contract requires real HTTP-host evidence that the
    // route binding, JWT authorization and claim derivation survive the wire.
    // These checks bind the production controllers through the shared
    // PortalApiMetadataHost with ISender replaced by a capturing fake: they
    // prove HTTP binding/authorization only — the handler is fake, so they
    // are NOT persistence or real-DB race proof (that stays in the
    // Mastermind runtime slot).

    [Fact]
    public async Task Put_post_through_the_host_uses_the_route_and_the_token_claim_not_forged_body_fields()
    {
        var sender = new HttpBindingSender();
        using WebApplicationFactory<Program> factory = HttpFactory(sender);
        using HttpClient client = CreateHostClient(factory);
        Guid authorId = CreateHostToken(client, AccountType.Family);
        Guid routeId = Guid.CreateVersion7();
        Guid forged = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.PutAsJsonAsync(
            $"/api/v1/community/posts/{routeId:D}",
            new
            {
                postId = forged,
                actingAuthorId = forged,
                authorId = Guid.CreateVersion7(),
                titleArabic = "عنوان",
                titleEnglish = "Title",
                contentArabic = "محتوى",
                contentEnglish = "Content",
                imageUrl = (string?)null,
                isAnonymous = false,
                categoryId = (Guid?)null,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        UpdatePostCommand command = Assert.IsType<UpdatePostCommand>(sender.LastUpdatePostCommand);
        Assert.Equal(routeId, command.PostId);
        Assert.Equal(authorId, command.ActingAuthorId); // the JWT sub claim
        Assert.NotEqual(forged, command.PostId);
        Assert.NotEqual(forged, command.ActingAuthorId);
    }

    [Fact]
    public async Task Admin_delete_through_the_host_by_a_ContentAdmin_token_returns_204_and_uses_the_claim()
    {
        var sender = new HttpBindingSender();
        using WebApplicationFactory<Program> factory = HttpFactory(sender);
        using HttpClient client = CreateHostClient(factory);
        Guid moderatorId = CreateHostToken(client, AccountType.ContentAdmin);
        Guid routeId = Guid.CreateVersion7();

        using HttpResponseMessage response = await client.DeleteAsync(
            $"/api/v1/admin/community/posts/{routeId:D}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AdminDeletePostCommand command =
            Assert.IsType<AdminDeletePostCommand>(sender.LastAdminDeletePostCommand);
        Assert.Equal(routeId, command.PostId);
        Assert.Equal(moderatorId, command.ModeratorId.Value); // the JWT sub claim
    }

    [Fact]
    public async Task Admin_delete_through_the_host_is_forbidden_without_the_moderation_role()
    {
        var sender = new HttpBindingSender();
        using WebApplicationFactory<Program> factory = HttpFactory(sender);

        using HttpClient supportAdmin = CreateHostClient(factory);
        CreateHostToken(supportAdmin, AccountType.SupportAdmin);
        using HttpResponseMessage supportResponse = await supportAdmin.DeleteAsync(
            $"/api/v1/admin/community/posts/{Guid.CreateVersion7():D}");
        Assert.Equal(HttpStatusCode.Forbidden, supportResponse.StatusCode);

        using HttpClient family = CreateHostClient(factory);
        CreateHostToken(family, AccountType.Family);
        using HttpResponseMessage familyResponse = await family.DeleteAsync(
            $"/api/v1/admin/community/posts/{Guid.CreateVersion7():D}");
        Assert.Equal(HttpStatusCode.Forbidden, familyResponse.StatusCode);

        // Authorization denies in the pipeline; the fake sender is never hit.
        Assert.False(sender.AnyRequest);
        Assert.Null(sender.LastAdminDeletePostCommand);
    }

    // ------------------------------------------------------------- helpers

    private static CommunityPostsController CreatePostsController(
        CommunityDbContext db, UserId actor) =>
        new(new CommunityPostManagementSender(db))
        {
            ControllerContext = ControllerContextFor(actor)
        };

    private static AdminCommunityPostsController CreateAdminController(
        CommunityDbContext db, UserId moderator) =>
        new(new CommunityPostManagementSender(db))
        {
            ControllerContext = ControllerContextFor(moderator)
        };

    private static CommentsController CreateCommentsController(
        CommunityDbContext db, UserId actor) =>
        new(new CommunityPostManagementSender(db))
        {
            ControllerContext = ControllerContextFor(actor)
        };

    private static ControllerContext ControllerContextFor(UserId actor) =>
        new()
        {
            HttpContext = new DefaultHttpContext
            {
                User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim(
                            System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                            actor.Value.ToString())],
                        "test"))
            }
        };

    private static Post CreatePublishedPost(UserId author)
    {
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, author, DateTime.UtcNow);
        post.Publish(UserId.New(), DateTime.UtcNow);
        return post;
    }

    private static CommunityDbContext CreateDb() => new(
        new DbContextOptionsBuilder<CommunityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    // ------------------------------------------- real-host HTTP harness
    //
    // Reuses the shared PortalApiMetadataHost fixture unmodified: only ISender
    // is replaced in the derived factory. Tokens come from the host's own
    // IAuthTokenService (issuer/audience/key overrides), so the JWT the
    // production JwtBearer pipeline validates is exactly what an issued
    // access token looks like in tests.

    private WebApplicationFactory<Program> HttpFactory(HttpBindingSender sender) =>
        _host.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISender>();
                services.AddSingleton<ISender>(sender);
            }));

    private static HttpClient CreateHostClient(WebApplicationFactory<Program> factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });

    /// <summary>
    /// Issues the access token through the host's IAuthTokenService, puts it
    /// on the client as the Bearer header and returns the user id (the sub
    /// claim value the handlers must see).
    /// </summary>
    private Guid CreateHostToken(HttpClient client, AccountType accountType)
    {
        User user = User.Create(
            FullName.Create("مستخدم"),
            FullName.Create("Portal User"),
            Email.Create($"portal.{Guid.CreateVersion7():N}@example.test"),
            PhoneNumber.Create("+201000003001"));
        user.AddAccount(accountType);
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken);
        return user.Id.Value;
    }
}

/// <summary>
/// Dispatches only the post-management requests to the real handlers over the
/// test context, so the controller bindings are exercised end to end.
/// </summary>
internal sealed class CommunityPostManagementSender(CommunityDbContext context) : ISender
{
    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        object? result = request switch
        {
            UpdatePostCommand command =>
                new UpdatePostCommandHandler(context).Handle(command, cancellationToken),
            DeletePostCommand command =>
                new DeletePostCommandHandler(context).Handle(command, cancellationToken),
            AdminDeletePostCommand command =>
                new AdminDeletePostCommandHandler(context).Handle(command, cancellationToken),
            GetCommunityAuthorProfilesQuery query =>
                new GetCommunityAuthorProfilesQueryHandler(context).Handle(query, cancellationToken),
            LikePostCommand command =>
                new LikePostCommandHandler(context).Handle(command, cancellationToken),
            UnlikePostCommand command =>
                new UnlikePostCommandHandler(context).Handle(command, cancellationToken),
            ToggleFavoritePostCommand command =>
                new ToggleFavoritePostCommandHandler(context).Handle(command, cancellationToken),
            LikeCommunityCommentCommand command =>
                new LikeCommunityCommentCommandHandler(context).Handle(command, cancellationToken),
            _ => throw new NotSupportedException($"Unexpected request {request.GetType().Name}.")
        };

        return (Task<TResponse>)result!;
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

/// <summary>
/// Capturing fake for the real-host HTTP tests: records the post-management
/// commands the production controllers dispatch over the wire and answers
/// with in-memory results only (no persistence, no real DB, no race proof).
/// </summary>
internal sealed class HttpBindingSender : ISender
{
    public UpdatePostCommand? LastUpdatePostCommand { get; private set; }

    public AdminDeletePostCommand? LastAdminDeletePostCommand { get; private set; }

    public bool AnyRequest { get; private set; }

    public Task<TResponse> Send<TResponse>(
        IRequest<TResponse> request,
        CancellationToken cancellationToken = default)
    {
        AnyRequest = true;
        return request switch
        {
            UpdatePostCommand command => Task.FromResult((TResponse)(object)
                Result<Post>.Success(CreateResponsePost(Record(command)))),
            AdminDeletePostCommand command => Task.FromResult((TResponse)(object)
                Result<bool>.Success(Record(command))),
            GetCommunityAuthorProfilesQuery => Task.FromResult((TResponse)(object)
                Result<IReadOnlyDictionary<Guid, CommunityAuthorProfile>>.Success(
                    new Dictionary<Guid, CommunityAuthorProfile>())),
            _ => throw new NotSupportedException($"Unexpected request {request.GetType().Name}.")
        };
    }

    private UpdatePostCommand Record(UpdatePostCommand command)
    {
        LastUpdatePostCommand = command;
        return command;
    }

    private bool Record(AdminDeletePostCommand command)
    {
        LastAdminDeletePostCommand = command;
        return true;
    }

    private static Post CreateResponsePost(UpdatePostCommand command) =>
        Post.Create(command.TitleArabic, command.TitleEnglish,
            command.ContentArabic, command.ContentEnglish, null, command.IsAnonymous,
            new UserId(command.ActingAuthorId), DateTime.UtcNow);

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
