using System.Reflection;
using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sanad.API;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.Controllers.Requests;
using Sanad.Modules.Cms.Application.Help;
using Sanad.Modules.Cms.Domain.Legal;
using Sanad.Modules.Cms.Infrastructure.Persistence;
using Sanad.Modules.Identity.Application.Authentication.Tokens;
using Sanad.Modules.Identity.Domain.Users;

namespace Sanad.UnitTests.API;

/// <summary>
/// Admin Help Center contact authorization contract. The two support-contact
/// actions carry the narrower <c>CmsHelpContact</c> policy (SuperAdmin +
/// SupportAdmin + ContentAdmin) while the controller-level <c>CmsContent</c>
/// policy stays on the FAQ actions, so SupportAdmin gains the contact card and
/// nothing else. The controller is driven for real over the Cms test context.
/// </summary>
public sealed class AdminHelpCenterContactAuthorizationTests
{
    private const string ContactRoute = "api/v1/admin/help-center";

    [Fact]
    public void SupportContactActions_CarryTheContactPolicy_AndFaqActionsKeepCmsContent()
    {
        var controllerAuthorize =
            typeof(AdminHelpCenterController).GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(controllerAuthorize);
        Assert.Equal(AuthorizationPolicies.CmsContent, controllerAuthorize!.Policy);
        Assert.Equal(
            ContactRoute,
            typeof(AdminHelpCenterController)
                .GetCustomAttribute<RouteAttribute>()!.Template);

        string[] contactActions =
        [
            nameof(AdminHelpCenterController.GetSupportContact),
            nameof(AdminHelpCenterController.UpsertSupportContact)
        ];
        string[] faqActions =
        [
            nameof(AdminHelpCenterController.CreateFaq),
            nameof(AdminHelpCenterController.ListFaqs),
            nameof(AdminHelpCenterController.GetFaqById),
            nameof(AdminHelpCenterController.UpdateFaq),
            nameof(AdminHelpCenterController.ActivateFaq),
            nameof(AdminHelpCenterController.DeactivateFaq)
        ];

        foreach (string action in contactActions)
        {
            var authorize = Action(action).GetCustomAttribute<AuthorizeAttribute>();

            Assert.NotNull(authorize);
            Assert.Equal(AuthorizationPolicies.CmsHelpContact, authorize!.Policy);
        }

        foreach (string action in faqActions)
        {
            // FAQ writes never receive the contact policy or an anonymous
            // override: the controller-level CmsContent policy still applies.
            Assert.Null(Action(action).GetCustomAttribute<AuthorizeAttribute>());
            Assert.Null(Action(action).GetCustomAttribute<AllowAnonymousAttribute>());
        }

        // The contact actions are not anonymous either.
        foreach (string action in contactActions)
        {
            Assert.Null(Action(action).GetCustomAttribute<AllowAnonymousAttribute>());
        }
    }

    [Fact]
    public async Task CmsHelpContact_AllowsSuperAdminSupportAdminAndContentAdminOnly()
    {
        AuthorizationPolicy policy = await ResolvePolicyAsync(
            AuthorizationPolicies.CmsHelpContact);

        Assert.Contains(
            policy.Requirements,
            requirement => requirement is DenyAnonymousAuthorizationRequirement);

        ClaimsAuthorizationRequirement access = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccessType);
        Assert.Equal<string>(
            [AuthAccessType.Normal.ToString()],
            access.AllowedValues!);

        ClaimsAuthorizationRequirement account = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccountType);

        Assert.NotNull(account.AllowedValues);
        Assert.Equal<string>(
            [
                AccountType.SuperAdmin.ToString(),
                AccountType.SupportAdmin.ToString(),
                AccountType.ContentAdmin.ToString()
            ],
            account.AllowedValues!);

        // Support staff are not SupportInbox here: this is the contact policy,
        // not the ticket policy. The three owners are exactly the approved set.
        Assert.Equal(3, account.AllowedValues!.Count());
    }

    [Fact]
    public async Task CmsContent_PolicyItselfIsNotWidenedToSupportAdmin()
    {
        AuthorizationPolicy policy = await ResolvePolicyAsync(
            AuthorizationPolicies.CmsContent);

        // CmsContent is composed identically by this registration: exactly one
        // access-requirement and one account-requirement on the same values.
        Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccessType);

        ClaimsAuthorizationRequirement account = Assert.Single(
            policy.Requirements.OfType<ClaimsAuthorizationRequirement>(),
            requirement => requirement.ClaimType == AuthClaimNames.AccountType);

        Assert.Equal<string>(
            [
                AccountType.SuperAdmin.ToString(),
                AccountType.ContentAdmin.ToString()
            ],
            account.AllowedValues!);
        Assert.DoesNotContain(
            AccountType.SupportAdmin.ToString(),
            account.AllowedValues!);
    }

    [Fact]
    public async Task GetSupportContact_NoRow_Returns200WithTwoNullFields()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        AdminHelpCenterController controller = CreateController(dbContext);

        IActionResult result = await controller.GetSupportContact(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SupportContactResponse>(ok.Value);

        Assert.Null(response.SupportPhone);
        Assert.Null(response.SupportEmail);

        // The empty-state read must not create the row.
        Assert.Empty(dbContext.SupportContacts);
    }

    [Fact]
    public async Task GetSupportContact_EmptyState_SerializesBothFieldsAsExplicitNulls()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        AdminHelpCenterController controller = CreateController(dbContext);

        IActionResult result = await controller.GetSupportContact(default);

        var ok = Assert.IsType<OkObjectResult>(result);

        string json = JsonSerializer.Serialize(
            ok.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        // Explicit nulls, not missing members: the documented web shape.
        Assert.True(root.TryGetProperty("supportPhone", out JsonElement phone));
        Assert.Equal(JsonValueKind.Null, phone.ValueKind);
        Assert.True(root.TryGetProperty("supportEmail", out JsonElement email));
        Assert.Equal(JsonValueKind.Null, email.ValueKind);
    }

    [Fact]
    public async Task UpsertSupportContact_SetOneThenClearIt_KeepsTheOtherValue()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        AdminHelpCenterController controller = CreateController(dbContext);

        IActionResult first = await controller.UpsertSupportContact(
            new UpdateSupportContactRequest("+201000000001", "support@sanad.example"),
            default);

        var firstOk = Assert.IsType<OkObjectResult>(first);
        var firstResponse = Assert.IsType<SupportContactResponse>(firstOk.Value);
        Assert.Equal("+201000000001", firstResponse.SupportPhone);
        Assert.Equal("support@sanad.example", firstResponse.SupportEmail);

        IActionResult second = await controller.UpsertSupportContact(
            new UpdateSupportContactRequest(null, "support@sanad.example"),
            default);

        var secondOk = Assert.IsType<OkObjectResult>(second);
        var secondResponse = Assert.IsType<SupportContactResponse>(secondOk.Value);
        Assert.Null(secondResponse.SupportPhone);
        Assert.Equal("support@sanad.example", secondResponse.SupportEmail);

        // Same singleton row, updated in place.
        Assert.Single(dbContext.SupportContacts);
    }

    [Fact]
    public async Task UpsertSupportContact_NullPair_ClearsBothFieldsAndKeepsTheRow()
    {
        await using CmsDbContext dbContext = CreateDbContext();
        AdminHelpCenterController controller = CreateController(dbContext);

        await controller.UpsertSupportContact(
            new UpdateSupportContactRequest("+201000000001", "support@sanad.example"),
            default);

        IActionResult cleared = await controller.UpsertSupportContact(
            new UpdateSupportContactRequest(null, null),
            default);

        var ok = Assert.IsType<OkObjectResult>(cleared);
        var response = Assert.IsType<SupportContactResponse>(ok.Value);

        Assert.Null(response.SupportPhone);
        Assert.Null(response.SupportEmail);
        Assert.Single(dbContext.SupportContacts);
    }

    [Fact]
    public void UpdateSupportContactRequest_BindsBothFieldsAsNullable()
    {
        // Positional record parameters without defaults are what made the
        // published OpenAPI list them as required; both are nullable now.
        PropertyInfo phone = typeof(UpdateSupportContactRequest)
            .GetProperty(nameof(UpdateSupportContactRequest.SupportPhone))!;
        PropertyInfo email = typeof(UpdateSupportContactRequest)
            .GetProperty(nameof(UpdateSupportContactRequest.SupportEmail))!;

        UpdateSupportContactRequest request = new(null, null);

        Assert.Null(request.SupportPhone);
        Assert.Null(request.SupportEmail);
        Assert.Equal(typeof(string), phone.PropertyType);
        Assert.Equal(typeof(string), email.PropertyType);
    }

    [Fact]
    public async Task FaqWrite_KeepsTheCmsContentAdminBinding()
    {
        // The FAQ path is unchanged end to end: the FAQ validator still names
        // the audience field, so SupportAdmin's 403 stays a pure policy result
        // and FAQ behavior did not move with the contact change.
        await using CmsDbContext dbContext = CreateDbContext();

        var controller = new AdminHelpCenterController(new CmsSender(dbContext))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        IActionResult result = await controller.CreateFaq(
            new CreateHelpFaqRequest(
                LegalAudience.Family,
                "سؤال",
                "Question",
                "إجابة",
                "Answer",
                0,
                true),
            default);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status201Created, created.StatusCode);
        Assert.Single(dbContext.HelpFaqs);
    }

    private static MethodInfo Action(string name) =>
        typeof(AdminHelpCenterController).GetMethod(name)!;

    private static async Task<AuthorizationPolicy> ResolvePolicyAsync(string name)
    {
        IServiceCollection services = new ServiceCollection();
        services.AddSanadApi(CreateConfiguration());

        using ServiceProvider provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true });

        IAuthorizationPolicyProvider policyProvider =
            provider.GetRequiredService<IAuthorizationPolicyProvider>();

        AuthorizationPolicy? policy = await policyProvider.GetPolicyAsync(name);
        Assert.NotNull(policy);
        return policy!;
    }

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(
                    "ConnectionStrings:IdentityDatabase",
                    "Host=localhost;Database=test;Username=test;Password=test"),
                new KeyValuePair<string, string?>("Identity:Jwt:Issuer", "Sanad.Api"),
                new KeyValuePair<string, string?>("Identity:Jwt:Audience", "Sanad.Clients"),
                new KeyValuePair<string, string?>(
                    "Identity:Jwt:SigningKey",
                    "12345678901234567890123456789012")
            ])
            .Build();

    private static AdminHelpCenterController CreateController(CmsDbContext dbContext) =>
        new(new CmsSender(dbContext))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static CmsDbContext CreateDbContext()
    {
        DbContextOptions<CmsDbContext> options =
            new DbContextOptionsBuilder<CmsDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;

        return new CmsDbContext(options);
    }

    /// <summary>
    /// Dispatches the Help Center requests to the real handlers over the test
    /// context, so the controller is exercised end to end.
    /// </summary>
    private sealed class CmsSender(CmsDbContext dbContext) : ISender
    {
        public Task<TResponse> Send<TResponse>(
            IRequest<TResponse> request,
            CancellationToken cancellationToken = default)
        {
            object? result = request switch
            {
                GetSupportContactQuery query =>
                    new GetSupportContactQueryHandler(dbContext)
                        .Handle(query, cancellationToken),
                UpsertSupportContactCommand command =>
                    new UpsertSupportContactCommandHandler(dbContext)
                        .Handle(command, cancellationToken),
                CreateHelpFaqCommand command =>
                    new CreateHelpFaqCommandHandler(dbContext)
                        .Handle(command, cancellationToken),
                _ => throw new NotSupportedException(
                    $"Unexpected request {request.GetType().Name}.")
            };

            return (Task<TResponse>)result!;
        }

        public Task Send<TRequest>(
            TRequest request,
            CancellationToken cancellationToken = default)
            where TRequest : IRequest =>
            throw new NotSupportedException();

        public Task<object?> Send(
            object request,
            CancellationToken cancellationToken = default) =>
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
