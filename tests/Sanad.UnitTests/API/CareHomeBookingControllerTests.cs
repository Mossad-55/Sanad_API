using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.API.ProblemDetail;
using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.UnitTests.API;

public sealed class CareHomeBookingControllerTests
{
    [Fact]
    public void Family_routes_require_family_access_and_expose_checkout_read_extension_and_intent()
    {
        var controller = typeof(FamilyCareHomeBookingsController);
        Assert.Equal(AuthorizationPolicies.FamilyAccess, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/family/care-home-bookings", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.NotNull(controller.GetMethod(nameof(FamilyCareHomeBookingsController.List)));
        Assert.NotNull(controller.GetMethod(nameof(FamilyCareHomeBookingsController.Detail)));
        Assert.Equal("checkout", controller.GetMethod(nameof(FamilyCareHomeBookingsController.Checkout))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/payments/intent", controller.GetMethod(nameof(FamilyCareHomeBookingsController.Payment))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/extensions", controller.GetMethod(nameof(FamilyCareHomeBookingsController.Extend))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/cancel", controller.GetMethod(nameof(FamilyCareHomeBookingsController.Cancel))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Owner_routes_require_owner_access_and_expose_mine_and_decision()
    {
        var controller = typeof(CareHomeBookingRequestsController);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/care-homes/booking-requests/mine", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.NotNull(controller.GetMethod(nameof(CareHomeBookingRequestsController.List)));
        Assert.Equal("{bookingId:guid}/decision", controller.GetMethod(nameof(CareHomeBookingRequestsController.Decide))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/assignment", controller.GetMethod(nameof(CareHomeBookingRequestsController.Assign))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/check-in", controller.GetMethod(nameof(CareHomeBookingRequestsController.CheckIn))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/check-out", controller.GetMethod(nameof(CareHomeBookingRequestsController.CheckOut))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/operational", controller.GetMethod(nameof(CareHomeBookingRequestsController.Operational))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/assignment/transfer", controller.GetMethod(nameof(CareHomeBookingRequestsController.Transfer))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/cancel", controller.GetMethod(nameof(CareHomeBookingRequestsController.Cancel))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/receipt", controller.GetMethod(nameof(CareHomeBookingRequestsController.Receipt))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/internal-notes", controller.GetMethod(nameof(CareHomeBookingRequestsController.Notes))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/internal-notes", controller.GetMethod(nameof(CareHomeBookingRequestsController.AddNote))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Owner_check_in_disputes_route_keeps_owner_policy_and_exposes_paged_mine_list()
    {
        var controller = typeof(CareHomeBookingRequestsController);
        MethodInfo method = controller.GetMethod(nameof(CareHomeBookingRequestsController.CheckInDisputes))!;
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        string routeTemplate = method.GetCustomAttribute<HttpGetAttribute>()!.Template!.Replace("~/", string.Empty);
        Assert.Equal("api/v1/care-homes/check-in-disputes/mine", routeTemplate);
        var producesAttributes = method.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute))
            .Cast<Microsoft.AspNetCore.Mvc.ProducesResponseTypeAttribute>().ToArray();
        Assert.Equal(typeof(Sanad.Modules.CareHomes.Application.Bookings.OwnerCareHomeCheckInDisputesResponse),
            Assert.Single(producesAttributes, a => a.StatusCode == StatusCodes.Status200OK).Type);
        foreach (string name in new[] { "page", "pageSize", "status" })
            Assert.Contains(method.GetParameters(), p => p.Name == name && p.GetCustomAttributes(typeof(FromQueryAttribute)).Any());
    }

    [Fact]
    public void Finance_and_note_routes_use_owner_and_operational_admin_policies()
    {
        var owner = typeof(CareHomeFinanceController);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, Assert.Single(owner.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/care-homes", Assert.Single(owner.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("dashboard/mine", owner.GetMethod(nameof(CareHomeFinanceController.Dashboard))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("revenue/mine", owner.GetMethod(nameof(CareHomeFinanceController.Revenue))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("revenue/mine/export.csv", owner.GetMethod(nameof(CareHomeFinanceController.Export))!.GetCustomAttribute<HttpGetAttribute>()!.Template);

        // FUR-05 owner settlement read: exact route, owner policy (class-level above) and
        // no caller-controlled facility scope parameter.
        MethodInfo payoutLedger = owner.GetMethod(nameof(CareHomeFinanceController.PayoutLedger))!;
        Assert.Equal("payouts/mine", payoutLedger.GetCustomAttribute<HttpGetAttribute>()!.Template);
        ParameterInfo[] payoutLedgerParameters = payoutLedger.GetParameters();
        Assert.DoesNotContain(payoutLedgerParameters, x => x.ParameterType != typeof(CancellationToken));
        Assert.DoesNotContain(payoutLedgerParameters, x => x.GetCustomAttribute<FromQueryAttribute>() is not null);

        var admin = typeof(AdminCareHomeFinanceController);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, Assert.Single(admin.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/care-homes", Assert.Single(admin.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("bookings/{bookingId:guid}/internal-notes", admin.GetMethod(nameof(AdminCareHomeFinanceController.AddNote))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void HC023_maintenance_mutations_require_owner_access_and_keep_routes_stable()
    {
        var controller = typeof(CareHomeInventoryController);
        Assert.Equal("api/v1/care-homes/inventory", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);

        var amend = controller.GetMethod(nameof(CareHomeInventoryController.AmendMaintenance))!;
        Assert.Equal("mine/maintenance/{id:guid}", amend.GetCustomAttribute<HttpPutAttribute>()!.Template);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, Assert.Single(amend.GetCustomAttributes<AuthorizeAttribute>()).Policy);

        var cancel = controller.GetMethod(nameof(CareHomeInventoryController.CancelMaintenance))!;
        Assert.Equal("mine/maintenance/{id:guid}/cancel", cancel.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, Assert.Single(cancel.GetCustomAttributes<AuthorizeAttribute>()).Policy);
    }

    [Fact]
    public void Family_confirmation_route_keeps_family_policy_and_template()
    {
        var controller = typeof(FamilyCareHomeBookingsController);
        Assert.Equal(AuthorizationPolicies.FamilyAccess, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("{bookingId:guid}/check-in-confirmation", controller.GetMethod(nameof(FamilyCareHomeBookingsController.ConfirmCheckIn))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Family_check_in_dispute_route_keeps_family_policy_and_template()
    {
        var controller = typeof(FamilyCareHomeBookingsController);
        Assert.Equal(AuthorizationPolicies.FamilyAccess, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("{bookingId:guid}/check-in-dispute", controller.GetMethod(nameof(FamilyCareHomeBookingsController.DisputeCheckIn))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Admin_dispute_routes_require_operational_admin_and_expose_list_and_resolution()
    {
        var controller = typeof(AdminCareHomeCheckInDisputesController);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/care-homes/check-in-disputes", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.NotNull(controller.GetMethod(nameof(AdminCareHomeCheckInDisputesController.List)));
        Assert.Equal("{caseId:guid}/resolve", controller.GetMethod(nameof(AdminCareHomeCheckInDisputesController.Resolve))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void Owner_check_in_disputes_endpoint_is_read_only_and_exposes_no_resolution_route()
    {
        var owner = typeof(CareHomeBookingRequestsController);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, Assert.Single(owner.GetCustomAttributes<AuthorizeAttribute>()).Policy);

        string[] postTemplates = owner.GetMethods()
            .Where(m => m.GetCustomAttribute<HttpPostAttribute>() is HttpPostAttribute post)
            .Select(m => postTemplate(m))
            .Where(t => t is not null)
            .Cast<string>()
            .ToArray();

        static string? postTemplate(MethodInfo m) => m.GetCustomAttribute<HttpPostAttribute>()?.Template?.ToLowerInvariant();

        Assert.DoesNotContain(postTemplates, t => t.Contains("check-in-dispute") || t.Contains("resolve"));
    }

    [Fact]
    public void Admin_refund_routes_require_operational_admin()
    {
        var controller = typeof(AdminCareHomeRefundsController);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/care-homes/bookings/{bookingId:guid}/refund", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("retry", controller.GetMethod(nameof(AdminCareHomeRefundsController.Retry))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("record-completed", controller.GetMethod(nameof(AdminCareHomeRefundsController.RecordCompleted))!.GetCustomAttribute<HttpPostAttribute>()!.Template);

        // Response contract pins (round-2 reconciliation): the retry really
        // can surface the mapper's 502 (Paymob.RefundRejected), while
        // record-completed never calls Paymob — so it must keep its actual
        // mapped set and must NOT advertise a 502 it can never answer.
        var retry = controller.GetMethod(nameof(AdminCareHomeRefundsController.Retry))!;
        Assert.Equal(
            new[] { StatusCodes.Status200OK, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict, StatusCodes.Status502BadGateway },
            StatusCodesOf(retry));
        var recordCompleted = controller.GetMethod(nameof(AdminCareHomeRefundsController.RecordCompleted))!;
        Assert.Equal(
            new[] { StatusCodes.Status200OK, StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict },
            StatusCodesOf(recordCompleted));
    }

    private static int[] StatusCodesOf(MethodInfo action) =>
        action.GetCustomAttributes<ProducesResponseTypeAttribute>(false)
            .Select(attribute => attribute.StatusCode)
            .OrderBy(code => code)
            .ToArray();

    [Fact]
    public void Webhook_controller_is_anonymous_and_keeps_paymob_route_stable()
    {
        var controller = typeof(PaymobWebhookController);
        Assert.NotEmpty(controller.GetCustomAttributes<AllowAnonymousAttribute>());
        Assert.Equal("api/v1/payments/webhooks", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("paymob", controller.GetMethod(nameof(PaymobWebhookController.HandlePaymob))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Theory]
    [InlineData("CareHomes.Bookings.NotFound", 404)]
    [InlineData("CareHomes.Bookings.InvalidState", 409)]
    [InlineData("CareHomes.Bookings.CapacityConflict", 409)]
    [InlineData("CareHomes.Bookings.PaymentConflict", 409)]
    [InlineData("CareHomes.Bookings.ChargesNotConfigured", 503)]
    [InlineData("CareHomes.Bookings.InvalidNote", 400)]
    [InlineData("CareHomes.Revenue.InvalidRange", 400)]
    [InlineData("CareHomes.Bookings.InvalidTransferDate", 400)]
    [InlineData("CareHomes.Bookings.TransferConflict", 409)]
    [InlineData("CareHomes.Bookings.RefundNotRetryable", 409)]
    [InlineData("CareHomes.Bookings.AssignmentConflict", 409)]
    [InlineData("CareHomes.Inventory.InvalidState", 409)]
    [InlineData("CareHomes.CheckInDispute.NotFound", 404)]
    [InlineData("CareHomes.CheckInDispute.InvalidState", 409)]
    [InlineData("CareHomes.CheckInDispute.InvalidReason", 400)]
    [InlineData("CareHomes.CheckInDispute.OpenConflict", 409)]
        [InlineData("CareHomes.CheckInDispute.InvalidQuery", 400)]
    public void Booking_error_codes_map_to_stable_http_statuses(string code, int expectedStatus)
    {
        var problem = ResultProblemDetailsMapper.Create(new Error(code, "internal detail"), new DefaultHttpContext());

        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
    }
}
