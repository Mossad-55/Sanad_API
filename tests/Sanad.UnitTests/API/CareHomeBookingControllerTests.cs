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
    public void Finance_and_note_routes_use_owner_and_operational_admin_policies()
    {
        var owner = typeof(CareHomeFinanceController);
        Assert.Equal(AuthorizationPolicies.CareHomeOwnerAccess, Assert.Single(owner.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/care-homes", Assert.Single(owner.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("dashboard/mine", owner.GetMethod(nameof(CareHomeFinanceController.Dashboard))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("revenue/mine", owner.GetMethod(nameof(CareHomeFinanceController.Revenue))!.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("revenue/mine/export.csv", owner.GetMethod(nameof(CareHomeFinanceController.Export))!.GetCustomAttribute<HttpGetAttribute>()!.Template);

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
    public void Admin_refund_routes_require_operational_admin()
    {
        var controller = typeof(AdminCareHomeRefundsController);
        Assert.Equal(AuthorizationPolicies.CareHomesOperationalAdmin, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/admin/care-homes/bookings/{bookingId:guid}/refund", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("retry", controller.GetMethod(nameof(AdminCareHomeRefundsController.Retry))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("record-completed", controller.GetMethod(nameof(AdminCareHomeRefundsController.RecordCompleted))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

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
    public void Booking_error_codes_map_to_stable_http_statuses(string code, int expectedStatus)
    {
        var problem = ResultProblemDetailsMapper.Create(new Error(code, "internal detail"), new DefaultHttpContext());

        Assert.Equal(expectedStatus, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
    }
}
