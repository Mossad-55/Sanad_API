using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.BuildingBlocks.Domain.ValueObjects;
using Ahlena.Modules.Community.Domain.Posts;
using Ahlena.Modules.Community.Infrastructure.Persistence;
using Ahlena.Modules.Identity.Application.Abstractions.Security;
using Ahlena.Modules.Identity.Domain.Users;

namespace Ahlena.UnitTests.API;

/// <summary>
/// HTTP-level facts for the M1 view route on the isolated in-process host: the
/// controller-level authentication gate answers 401, an invisible/unknown post
/// answers 404, and a published post returns the stored counter with a repeat
/// returning the same count.
/// </summary>
public sealed class CommunityPostViewApiTests : IClassFixture<PortalApiMetadataHost>
{
    private readonly PortalApiMetadataHost _host;

    public CommunityPostViewApiTests(PortalApiMetadataHost host) => _host = host;

    [Fact]
    public async Task Record_view_without_a_token_returns_unauthorized()
    {
        using HttpClient client = _host.CreatePortalClient();

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/community/posts/{Guid.CreateVersion7():D}/view",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Record_view_for_an_unknown_post_returns_not_found()
    {
        using HttpClient client = _host.CreatePortalClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage response = await client.PostAsync(
            $"/api/v1/community/posts/{Guid.CreateVersion7():D}/view",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Record_view_counts_once_and_reports_the_current_counter()
    {
        Guid postId = await SeedPublishedPostAsync();
        using HttpClient client = _host.CreatePortalClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateAccessToken());

        using HttpResponseMessage first = await client.PostAsync(
            $"/api/v1/community/posts/{postId:D}/view",
            content: null);
        using HttpResponseMessage repeat = await client.PostAsync(
            $"/api/v1/community/posts/{postId:D}/view",
            content: null);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        CommunityPostViewResponse? firstBody =
            await first.Content.ReadFromJsonAsync<CommunityPostViewResponse>();
        Assert.NotNull(firstBody);
        Assert.Equal(postId, firstBody!.PostId);
        Assert.Equal(1, firstBody.ViewsCount);

        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        CommunityPostViewResponse? repeatBody =
            await repeat.Content.ReadFromJsonAsync<CommunityPostViewResponse>();
        Assert.NotNull(repeatBody);
        Assert.Equal(1, repeatBody!.ViewsCount);
    }

    private async Task<Guid> SeedPublishedPostAsync()
    {
        using IServiceScope scope = _host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommunityDbContext>();
        DateTime now = DateTime.UtcNow;
        var authorId = UserId.New();
        Post post = Post.Create(
            "عنوان", "Title", "محتوى", "Content", null, false, authorId, now);
        post.Publish(UserId.New(), now);
        db.Posts.Add(post);
        await db.SaveChangesAsync();
        return post.Id.Value;
    }

    private string CreateAccessToken()
    {
        User user = User.Create(
            FullName.Create("مستخدم"),
            FullName.Create("Viewer"),
            Email.Create("community.viewer@example.test"),
            PhoneNumber.Create("+201000004001"));
        user.AddAccount(AccountType.Family);
        IAuthTokenService tokens = _host.Services.GetRequiredService<IAuthTokenService>();
        return tokens.GenerateAccessToken(user, DateTime.UtcNow.AddSeconds(-5)).PlainTextToken;
    }
}
