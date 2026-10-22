using System.Net;
using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.BuildingBlocks.Application.Abstractions;
using Sanad.Modules.Identity.Application.Support;
using Sanad.Modules.Identity.Domain.Support;
using Sanad.Modules.Identity.Infrastructure.Persistence;

namespace Sanad.UnitTests.API;

/// <summary>
/// The anonymous partner-contact surface and its admin read. The controller is
/// driven for real: the statuses, the error shapes and the retry header are
/// the ones the app and the support inbox depend on, and the viewer of the
/// admin read is the existing support-inbox policy — no new policy.
/// </summary>
public sealed class PartnerContactControllerTests
{
    private static readonly DateTime UtcNow =
        new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Submit_accepts_an_anonymous_request_and_returns_the_receipt()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(dbContext);

        IActionResult result = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                "Hassani Care",
                "nour@example.com",
                "Partnership",
                "We would like to discuss a partnership."),
            "key-1",
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var receipt = Assert.IsType<PartnerContactResponse>(ok.Value);
        Assert.Equal(UtcNow, receipt.SubmittedOnUtc);

        // The request data is persisted as request data; no account exists.
        Assert.Single(dbContext.PartnerContacts);
        Assert.Empty(dbContext.Users);
    }

    [Fact]
    public async Task Submit_derives_the_client_key_from_the_request_and_never_from_the_body()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(
            dbContext,
            IPAddress.Parse("203.0.113.7"));

        await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership."),
            "key-1",
            default);

        var contact = await dbContext.PartnerContacts.SingleAsync();

        // The client key is a one-way digest of the request address, never the
        // address itself and never something the caller can choose.
        Assert.NotEqual("203.0.113.7", contact.ClientKey);
        Assert.Equal(64, contact.ClientKey.Length);
        Assert.Matches("^[0-9A-F]{64}$", contact.ClientKey);
    }

    [Fact]
    public async Task Submit_rejects_a_request_without_an_idempotency_key()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(dbContext);

        IActionResult result = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership."),
            null,
            default);

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, badRequest.StatusCode);
        Assert.Empty(dbContext.PartnerContacts);
    }

    [Fact]
    public async Task Submit_answers_a_reused_idempotency_key_with_different_content_as_a_conflict()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(dbContext);

        await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership."),
            "key-1",
            default);

        IActionResult result = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "A completely different message body."),
            "key-1",
            default);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Single(dbContext.PartnerContacts);
    }

    [Fact]
    public async Task Submit_answers_the_rate_limit_with_a_retry_after_header()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(dbContext);

        for (int index = 0; index < SubmitPartnerContactCommandHandler.MaxSubmissionsPerWindowPerClient; index++)
        {
            await controller.SubmitPartnerContact(
                new SubmitPartnerContactRequest(
                    "Nour Hassan",
                    null,
                    "nour@example.com",
                    null,
                    "We would like to discuss a partnership."),
                $"key-{index}",
                default);
        }

        IActionResult result = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership."),
            "key-over-the-limit",
            default);

        var limited = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status429TooManyRequests, limited.StatusCode);
        Assert.True(controller.Response.Headers.ContainsKey("Retry-After"));
    }

    [Fact]
    public void The_submit_route_is_anonymous_and_the_admin_route_keeps_the_support_inbox_policy()
    {
        Type partner = typeof(PartnerContactController);
        Assert.Null(partner.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(
            "api/v1/partner-contact",
            partner.GetCustomAttribute<RouteAttribute>()!.Template);

        // The submit action never reads a token: the route carries the
        // explicit anonymous marker and no authorize attribute.
        MethodInfo submit = typeof(PartnerContactController).GetMethod(
            nameof(PartnerContactController.SubmitPartnerContact))!;
        Assert.NotNull(partner.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(submit.GetCustomAttribute<AuthorizeAttribute>());

        // The admin read is on the existing admin support surface with the
        // existing policy — no new policy is invented.
        Type admin = typeof(AdminSupportTicketsController);
        Assert.Equal(
            AuthorizationPolicies.SupportInboxAdmin,
            admin.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
    }

    [Fact]
    public async Task The_receipt_is_serialized_in_the_documented_web_shape()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(dbContext);

        IActionResult result = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                "Hassani Care",
                "nour@example.com",
                "Partnership",
                "We would like to discuss a partnership."),
            "key-1",
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var receipt = Assert.IsType<PartnerContactResponse>(ok.Value);

        // The wire shape the app and the docs rely on: the strongly typed id
        // is an object with a "value" member, and the instant is UTC ISO-8601.
        string json = System.Text.Json.JsonSerializer.Serialize(
            receipt,
            new System.Text.Json.JsonSerializerOptions(
                System.Text.Json.JsonSerializerDefaults.Web));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal(
            receipt.ContactId.Value.ToString(),
            root.GetProperty("contactId").GetProperty("value").GetString());
        Assert.Equal(
            receipt.SubmittedOnUtc,
            root.GetProperty("submittedOnUtc").GetDateTime());
        Assert.Equal(
            "2026-10-21T12:00:00Z",
            root.GetProperty("submittedOnUtc").GetString());
    }

    [Fact]
    public async Task The_admin_read_returns_the_paged_partner_contacts()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        AdminSupportTicketsController controller = new AdminSupportTicketsController(
            new PartnerContactSender(dbContext))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        await new SubmitPartnerContactCommandHandler(dbContext, new FixedDateTimeProvider())
            .Handle(
                new SubmitPartnerContactCommand(
                    "Nour Hassan",
                    "Hassani Care",
                    "nour@example.com",
                    "Partnership",
                    "We would like to discuss a partnership.",
                    "key-1",
                    "client-1"),
                CancellationToken.None);

        IActionResult result = await controller.ListPartnerContacts(
            page: 1,
            pageSize: 25,
            cancellationToken: default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<AdminPartnerContactsPage>(ok.Value);
        AdminPartnerContactListItem item = Assert.Single(page.Items);
        Assert.Equal("Nour Hassan", item.Name);
        Assert.Equal("Hassani Care", item.Company);
        Assert.Equal("nour@example.com", item.Email);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Submit_forwards_the_optional_contact_fields_to_the_persisted_row()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(dbContext);

        IActionResult result = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "نور حسن",
                "Hassani Care",
                "nour@example.com",
                "Partnership",
                "We would like to discuss a partnership.",
                "  +20 100 123 4567  ",
                "  دار رعاية  ",
                "  القاهرة  "),
            "key-1",
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.IsType<PartnerContactResponse>(ok.Value);

        PartnerContact stored = await dbContext.PartnerContacts.SingleAsync();
        Assert.Equal("+20 100 123 4567", stored.Phone);
        Assert.Equal("دار رعاية", stored.CompanyType);
        Assert.Equal("القاهرة", stored.City);
    }

    [Fact]
    public async Task Submit_stores_a_blank_optional_field_as_null_and_conflicts_when_only_it_differs()
    {
        await using IdentityDbContext dbContext = CreateDbContext();
        PartnerContactController controller = CreateController(dbContext);

        IActionResult first = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership.",
                "   ",
                null,
                null),
            "key-1",
            default);

        Assert.IsType<OkObjectResult>(first);
        PartnerContact stored = await dbContext.PartnerContacts.SingleAsync();
        Assert.Null(stored.Phone);
        Assert.Null(stored.CompanyType);
        Assert.Null(stored.City);

        // The same key with only the phone changed is the same content
        // conflict as any other difference.
        IActionResult conflicting = await controller.SubmitPartnerContact(
            new SubmitPartnerContactRequest(
                "Nour Hassan",
                null,
                "nour@example.com",
                null,
                "We would like to discuss a partnership.",
                "+20 100 123 4567",
                null,
                null),
            "key-1",
            default);

        var conflict = Assert.IsType<ObjectResult>(conflicting);
        Assert.Equal(StatusCodes.Status409Conflict, conflict.StatusCode);
        Assert.Single(dbContext.PartnerContacts);
    }

    private static PartnerContactController CreateController(
        IdentityDbContext dbContext,
        IPAddress? remoteAddress = null)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = remoteAddress;

        return new PartnerContactController(new PartnerContactSender(dbContext))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            }
        };
    }

    private static IdentityDbContext CreateDbContext()
    {
        DbContextOptions<IdentityDbContext> options =
            new DbContextOptionsBuilder<IdentityDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new IdentityDbContext(options);
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        private static readonly DateTime Value =
            new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

        public DateTime UtcNow => Value;
    }

    /// <summary>
    /// Dispatches the partner-contact requests to the real handlers over the
    /// test context, so the controller is exercised end to end.
    /// </summary>
    private sealed class PartnerContactSender(IdentityDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object? result = request switch
            {
                SubmitPartnerContactCommand command =>
                    new SubmitPartnerContactCommandHandler(dbContext, new FixedDateTimeProvider())
                        .Handle(command, cancellationToken),
                GetAdminPartnerContactsQuery query =>
                    new GetAdminPartnerContactsQueryHandler(dbContext)
                        .Handle(query, cancellationToken),
                GetAdminPartnerContactByIdQuery query =>
                    new GetAdminPartnerContactByIdQueryHandler(dbContext)
                        .Handle(query, cancellationToken),
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
}
