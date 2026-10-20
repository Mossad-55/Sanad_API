using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.Modules.Cms.Application.Help;

namespace Sanad.UnitTests.API;

/// <summary>
/// F5 support HTTP surface. The admin inbox is pinned to the SuperAdmin +
/// SupportAdmin policy and four routes; the authenticated ticket reads stay
/// on NormalAccess; and the only anonymous surface is the contact card, which
/// exposes nothing beyond the phone number and email.
/// </summary>
public sealed class SupportInboxApiTests
{
    [Fact]
    public void AdminSupportTicketsController_IsPinnedToTheSupportInboxPolicyAndRoutes()
    {
        // The wire name is pinned: the attribute, the constant and the DI
        // registration all key on this exact string.
        Assert.Equal(
            "SupportInboxAdmin",
            AuthorizationPolicies.SupportInboxAdmin);

        Type controller = typeof(AdminSupportTicketsController);

        Assert.Equal(
            "api/v1/admin/support-tickets",
            controller.GetCustomAttribute<RouteAttribute>()!.Template);
        Assert.Equal(
            AuthorizationPolicies.SupportInboxAdmin,
            controller.GetCustomAttribute<AuthorizeAttribute>()!.Policy);

        MethodInfo list = Method(
            controller,
            nameof(AdminSupportTicketsController.ListTickets));
        MethodInfo detail = Method(
            controller,
            nameof(AdminSupportTicketsController.GetTicketById));
        MethodInfo status = Method(
            controller,
            nameof(AdminSupportTicketsController.ChangeStatus));
        MethodInfo reply = Method(
            controller,
            nameof(AdminSupportTicketsController.Reply));

        Assert.NotNull(list.GetCustomAttribute<HttpGetAttribute>());
        Assert.Null(list.GetCustomAttribute<HttpPostAttribute>());

        Assert.Equal(
            "{id:guid}",
            detail.GetCustomAttribute<HttpGetAttribute>()!.Template);

        Assert.Equal(
            "{id:guid}/status",
            status.GetCustomAttribute<HttpPostAttribute>()!.Template);

        Assert.Equal(
            "{id:guid}/replies",
            reply.GetCustomAttribute<HttpPostAttribute>()!.Template);

        // Every action inherits the class policy; none may widen it.
        foreach (MethodInfo action in new[] { list, detail, status, reply })
        {
            AuthorizeAttribute? authorize =
                action.GetCustomAttribute<AuthorizeAttribute>();

            Assert.True(
                authorize is null ||
                    authorize.Policy == AuthorizationPolicies.SupportInboxAdmin,
                $"{action.Name} must not widen the support inbox policy.");
        }

        // A refused transition and a reply to a closed ticket are conflicts.
        Assert.Contains(
            status.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status409Conflict);
        Assert.Contains(
            reply.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == StatusCodes.Status409Conflict);
    }

    [Fact]
    public void SupportController_KeepsTheAnonymousContactCardAndAuthenticatedTicketReads()
    {
        Type controller = typeof(SupportController);

        Assert.Equal(
            "api/v1/support",
            controller.GetCustomAttribute<RouteAttribute>()!.Template);

        // Routes are authorised per action, never by a class default.
        Assert.Null(controller.GetCustomAttribute<AuthorizeAttribute>());

        MethodInfo contact = Method(
            controller,
            nameof(SupportController.GetSupportContact));

        Assert.NotNull(contact.GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(contact.GetCustomAttribute<AuthorizeAttribute>());
        Assert.Equal(
            "contact",
            contact.GetCustomAttribute<HttpGetAttribute>()!.Template);

        MethodInfo submit = Method(
            controller,
            nameof(SupportController.SubmitSupportRequest));
        MethodInfo myTickets = Method(
            controller,
            nameof(SupportController.GetMyTickets));
        MethodInfo myTicket = Method(
            controller,
            nameof(SupportController.GetMyTicketById));

        Assert.Equal(
            "contact",
            submit.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(
            "tickets",
            myTickets.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal(
            "tickets/{id:guid}",
            myTicket.GetCustomAttribute<HttpGetAttribute>()!.Template);

        foreach (MethodInfo action in new[] { submit, myTickets, myTicket })
        {
            Assert.Null(action.GetCustomAttribute<AllowAnonymousAttribute>());
            Assert.Equal(
                AuthorizationPolicies.NormalAccess,
                action.GetCustomAttribute<AuthorizeAttribute>()!.Policy);
        }
    }

    [Fact]
    public void PublicSupportContactResponse_ExposesNothingBeyondTheContactCard()
    {
        Assert.Equal(
            new[] { "SupportEmail", "SupportPhone" },
            typeof(PublicSupportContactResponse)
                .GetProperties()
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));
    }

    private static MethodInfo Method(Type controller, string name)
    {
        return controller.GetMethod(name)
            ?? throw new InvalidOperationException(
                $"{controller.Name}.{name} was not found.");
    }
}
