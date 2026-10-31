using System.Reflection;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Authorization;
using Ahlena.API.Controllers;
using Ahlena.BuildingBlocks.Application.Abstractions;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Cms.Application.Settings;

namespace Ahlena.UnitTests.API;

/// <summary>
/// The site-settings surfaces: the CMS-admin authoring surface and the
/// anonymous public read. The authorization contract (the existing CMS content
/// policy and the explicit anonymous route) and the returned statuses and
/// bodies are pinned here.
/// </summary>
public sealed class SiteSettingsControllerTests
{
    [Fact]
    public void Admin_controller_uses_the_existing_cms_content_policy_and_route()
    {
        var authorize = Assert.Single(typeof(AdminSiteSettingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true)
            .Cast<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.CmsContent, authorize.Policy);

        var route = Assert.Single(typeof(AdminSiteSettingsController)
            .GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>());
        Assert.Equal("api/v1/admin/site-settings", route.Template);
    }

    [Fact]
    public void Public_controller_is_anonymous_and_exposes_one_read_route()
    {
        Assert.Empty(typeof(PublicSiteSettingsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true));

        var get = Assert.Single(
            typeof(PublicSiteSettingsController)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance),
            method => method.Name == "Get");
        Assert.NotNull(Assert.Single(get.GetCustomAttributes(typeof(AllowAnonymousAttribute), true)));

        var route = Assert.Single(typeof(PublicSiteSettingsController)
            .GetCustomAttributes(typeof(RouteAttribute), true)
            .Cast<RouteAttribute>());
        Assert.Equal("api/v1/site-settings", route.Template);
    }

    [Fact]
    public async Task Admin_update_returns_the_authored_settings_with_200()
    {
        var sender = new StubSender(Result<SiteSettingsAdminResponse>.Success(
            new SiteSettingsAdminResponse(
                [new SiteSettingsNavbarLink("library", "المكتبة", "Library", "/library")],
                new SiteSettingsSeoResponse("أهلنا", "Ahlena", null, null),
                DateTime.UtcNow,
                DateTime.UtcNow)));
        AdminSiteSettingsController controller = CreateAdminController(sender);

        IActionResult result = await controller.Update(
            new UpdateSiteSettingsRequest(
                [new SiteSettingsNavbarLinkRequest("library", "المكتبة", "Library", "/library")],
                new SiteSettingsSeoRequest("أهلنا", "Ahlena")),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SiteSettingsAdminResponse>(ok.Value);
        Assert.Equal("library", Assert.Single(response.NavbarLinks).Key);
        Assert.Equal("المكتبة", response.NavbarLinks[0].ArabicLabel);
        Assert.Equal("أهلنا", response.Seo!.ArabicTitle);

        // The authored request reached the command unchanged.
        var command = Assert.IsType<UpdateSiteSettingsCommand>(sender.LastRequest);
        Assert.Equal("library", Assert.Single(command.NavbarLinks!).Key);
        Assert.Equal("/library", command.NavbarLinks![0].Url);
        Assert.Equal("أهلنا", command.Seo!.ArabicTitle);
    }

    [Fact]
    public async Task Admin_read_maps_a_missing_row_to_404_and_a_concurrent_write_to_409()
    {
        AdminSiteSettingsController missing = CreateAdminController(
            new StubSender(Result<SiteSettingsAdminResponse>.Failure(SiteSettingsErrors.NotFound)));

        IActionResult notFound = await missing.Get(default);
        var notFoundProblem = Assert.IsType<ObjectResult>(notFound);
        Assert.Equal(StatusCodes.Status404NotFound, notFoundProblem.StatusCode);
        Assert.Equal(
            SiteSettingsErrors.NotFoundCode,
            Assert.IsType<ProblemDetails>(notFoundProblem.Value).Extensions["code"]);

        AdminSiteSettingsController conflicting = CreateAdminController(
            new StubSender(Result<SiteSettingsAdminResponse>.Failure(SiteSettingsErrors.Conflict)));

        IActionResult conflict = await conflicting.Update(
            new UpdateSiteSettingsRequest(),
            default);
        var conflictProblem = Assert.IsType<ObjectResult>(conflict);
        Assert.Equal(StatusCodes.Status409Conflict, conflictProblem.StatusCode);
        Assert.Equal(
            SiteSettingsErrors.ConflictCode,
            Assert.IsType<ProblemDetails>(conflictProblem.Value).Extensions["code"]);
    }

    [Fact]
    public async Task Admin_update_maps_invalid_content_to_400()
    {
        AdminSiteSettingsController controller = CreateAdminController(
            new StubSender(Result<SiteSettingsAdminResponse>.Failure(
                SiteSettingsErrors.Invalid("Navbar link url must be a root-relative path or an absolute http(s) url."))));

        IActionResult result = await controller.Update(
            new UpdateSiteSettingsRequest(),
            default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            SiteSettingsErrors.InvalidCode,
            Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);
    }

    [Fact]
    public async Task Admin_update_refuses_a_null_navbar_element_with_the_documented_400()
    {
        var sender = new StubSender(Result<SiteSettingsAdminResponse>.Success(
            new SiteSettingsAdminResponse(
                [new SiteSettingsNavbarLink("library", "المكتبة", "Library", "/library")],
                new SiteSettingsSeoResponse("أهلنا", "Ahlena", null, null),
                DateTime.UtcNow,
                DateTime.UtcNow)));
        AdminSiteSettingsController controller = CreateAdminController(sender);

        IActionResult result = await controller.Update(
            new UpdateSiteSettingsRequest(
                [
                    new SiteSettingsNavbarLinkRequest("library", "المكتبة", "Library", "/library"),
                    null
                ],
                new SiteSettingsSeoRequest("أهلنا", "Ahlena")),
            default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, problem.StatusCode);
        Assert.Equal(
            SiteSettingsErrors.InvalidCode,
            Assert.IsType<ProblemDetails>(problem.Value).Extensions["code"]);

        // The write was never dispatched, so the stored row cannot change.
        Assert.Null(sender.LastRequest);
    }

    [Fact]
    public async Task Public_read_returns_200_with_the_published_shape()
    {
        var sender = new StubSender(Result<PublicSiteSettingsResponse>.Success(
            new PublicSiteSettingsResponse(
                [new SiteSettingsNavbarLink("library", "المكتبة", "Library", "/library")],
                null)));
        PublicSiteSettingsController controller = CreatePublicController(sender);

        IActionResult result = await controller.Get(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<PublicSiteSettingsResponse>(ok.Value);
        Assert.Equal("library", Assert.Single(response.NavbarLinks).Key);
        Assert.Null(response.Seo);
        Assert.IsType<GetPublicSiteSettingsQuery>(sender.LastRequest);
    }

    private static AdminSiteSettingsController CreateAdminController(ISender sender)
    {
        var controller = new AdminSiteSettingsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }

    private static PublicSiteSettingsController CreatePublicController(ISender sender)
    {
        var controller = new PublicSiteSettingsController(sender)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        return controller;
    }

    private sealed class StubSender(object response) : ISender
    {
        public object? LastRequest { get; private set; }

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult((TResponse)response);
        }

        public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
            where TRequest : IRequest => throw new NotSupportedException();

        public Task<object?> Send(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
