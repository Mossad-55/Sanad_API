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
    public void Family_routes_require_family_access_and_expose_checkout_read_and_intent()
    {
        var controller = typeof(FamilyCareHomeBookingsController);
        Assert.Equal(AuthorizationPolicies.FamilyAccess, Assert.Single(controller.GetCustomAttributes<AuthorizeAttribute>()).Policy);
        Assert.Equal("api/v1/family/care-home-bookings", Assert.Single(controller.GetCustomAttributes<RouteAttribute>()).Template);
        Assert.NotNull(controller.GetMethod(nameof(FamilyCareHomeBookingsController.List)));
        Assert.NotNull(controller.GetMethod(nameof(FamilyCareHomeBookingsController.Detail)));
        Assert.Equal("checkout", controller.GetMethod(nameof(FamilyCareHomeBookingsController.Checkout))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{bookingId:guid}/payments/intent", controller.GetMethod(nameof(FamilyCareHomeBookingsController.Payment))!.GetCustomAttribute<HttpPostAttribute>()!.Template);
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
