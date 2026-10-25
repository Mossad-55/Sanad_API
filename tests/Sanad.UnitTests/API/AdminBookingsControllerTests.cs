using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API;
using Sanad.API.Authorization;
using Sanad.API.Controllers;
using Sanad.Modules.Families.Application.Bookings;
using Sanad.Modules.Families.Domain.Bookings;
using Xunit;

namespace Sanad.UnitTests.API;

/// <summary>
/// W1 admin booking discovery: the HTTP surface of <c>AdminBookingsController</c>, pinned by
/// reflection so the route, authorization policy, binding and produced-status contract cannot
/// drift without a failing test. The policy is the existing caregivers-admin policy (unchanged);
/// the one visible change is the optional <c>status</c> query binding on the list action.
/// <para>
/// The mapped outcomes listed here document the corrected mapper behavior (portal second
/// pass B1): <c>Paymob.RefundRejected</c> produces a 502 Bad Gateway and
/// <c>Paymob.RefundOutcomeUnknown</c> a 409 Conflict. Both keep their stable error code on
/// the wire, and status alone grants no retry permission: the booking's ledger/state fences
/// stay authoritative.
/// </para>
/// <para>Executed in the combined focused W1–W4 suite.</para>
/// </summary>
public sealed class AdminBookingsControllerTests
{
    [Fact]
    public void Controller_IsPinnedToTheAdminRoute_AndTheCaregiversAdminPolicy()
    {
        var controllerType = typeof(AdminBookingsController);

        var route = Assert.Single(controllerType.GetCustomAttributes<RouteAttribute>());
        Assert.Equal("api/v1/admin/bookings", route.Template);

        var authorize = Assert.Single(controllerType.GetCustomAttributes<AuthorizeAttribute>());
        Assert.Equal(AuthorizationPolicies.CaregiversAdmin, authorize.Policy);
    }

    [Fact]
    public void ListBookings_BindsAnOptionalStatusFilter_AfterTheExistingQueryInputs()
    {
        ParameterInfo[] parameters = typeof(AdminBookingsController)
            .GetMethod(nameof(AdminBookingsController.ListBookings))!
            .GetParameters();

        Assert.Equal(5, parameters.Length);

        Assert.Equal("page", parameters[0].Name);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.True(parameters[0].HasDefaultValue);
        Assert.Equal(1, parameters[0].DefaultValue);

        Assert.Equal("pageSize", parameters[1].Name);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
        Assert.Equal(10, parameters[1].DefaultValue);

        Assert.Equal("finance", parameters[2].Name);
        Assert.Equal(typeof(AdminBookingFinanceFilter), parameters[2].ParameterType);
        Assert.Equal(AdminBookingFinanceFilter.All, parameters[2].DefaultValue);

        Assert.Equal("status", parameters[3].Name);
        Assert.Equal(typeof(BookingStatus?), parameters[3].ParameterType);
        Assert.True(parameters[3].HasDefaultValue);
        Assert.Null(parameters[3].DefaultValue);
        Assert.NotNull(parameters[3].GetCustomAttribute<FromQueryAttribute>());

        Assert.Equal("cancellationToken", parameters[4].Name);

        // The query carries the filter to the handler so the coded 400 contract can validate it.
        Assert.Equal(
            typeof(BookingStatus?),
            typeof(ListAdminBookingsQuery).GetProperty("Status")!.PropertyType);
    }

    [Fact]
    public void ListBookings_ProducesOnlyThePagedResult()
    {
        ProducingFor(
            nameof(AdminBookingsController.ListBookings),
            HttpVerb.Get,
            expected: [(StatusCodes.Status200OK, typeof(PagedAdminBookings))],
            routeTemplate: null);
    }

    [Fact]
    public void ListCancellations_KeepsItsRoute_AndProducedType()
    {
        ProducingFor(
            nameof(AdminBookingsController.ListCancellations),
            HttpVerb.Get,
            expected: [(StatusCodes.Status200OK, typeof(PagedAdminCancellations))],
            routeTemplate: "cancellations");
    }

    [Fact]
    public void GetBooking_KeepsItsRoute_AndProducedType()
    {
        ProducingFor(
            nameof(AdminBookingsController.GetBooking),
            HttpVerb.Get,
            expected: [(StatusCodes.Status200OK, typeof(BookingDetailResponse))],
            routeTemplate: "{bookingId:guid}");
    }

    [Fact]
    public void RefundBooking_KeepsItsRoute_AndProducedType()
    {
        ProducingFor(
            nameof(AdminBookingsController.RefundBooking),
            HttpVerb.Post,
            // 502 added with the B1 mapping: Paymob.RefundRejected surfaces as
            // Bad Gateway on the retry too.
            expected:
            [
                (StatusCodes.Status200OK, typeof(BookingDetailResponse)),
                (StatusCodes.Status502BadGateway, null),
            ],
            routeTemplate: "{bookingId:guid}/refund");
    }

    [Fact]
    public void RecordOutcome_KeepsItsStatusCodeSet()
    {
        ProducingFor(
            nameof(AdminBookingsController.RecordOutcome),
            HttpVerb.Post,
            // 502 added with the B1 mapping: Paymob.RefundRejected (definitive
            // provider refusal) surfaces as Bad Gateway; the unknown outcome
            // keeps the existing 409.
            expected:
            [
                (StatusCodes.Status204NoContent, null),
                (StatusCodes.Status400BadRequest, null),
                (StatusCodes.Status404NotFound, null),
                (StatusCodes.Status409Conflict, null),
                (StatusCodes.Status502BadGateway, null),
                (StatusCodes.Status503ServiceUnavailable, null),
            ],
            routeTemplate: "{bookingId:guid}/outcome");
    }

    [Fact]
    public void ReconcileRefundAmbiguity_KeepsItsStatusCodeSet()
    {
        ProducingFor(
            nameof(AdminBookingsController.ReconcileRefundAmbiguity),
            HttpVerb.Post,
            expected:
            [
                (StatusCodes.Status204NoContent, null),
                (StatusCodes.Status400BadRequest, null),
                (StatusCodes.Status404NotFound, null),
                (StatusCodes.Status409Conflict, null),
            ],
            routeTemplate: "{bookingId:guid}/refund/reconcile");
    }

    [Fact]
    public void AdminRecordBookingOutcomeRequest_KeepsItsWireShape()
    {
        ConstructorInfo constructor = Assert.Single(
            typeof(AdminRecordBookingOutcomeRequest).GetConstructors());

        ParameterInfo[] wire = constructor.GetParameters();

        Assert.Equal(6, wire.Length);

        Assert.Equal("OutcomeReason", wire[0].Name);
        Assert.Equal(typeof(int), wire[0].ParameterType);
        Assert.False(wire[0].HasDefaultValue);

        Assert.Equal("Reason", wire[1].Name);
        Assert.Equal(typeof(string), wire[1].ParameterType);

        Assert.Equal("ReasonCategory", wire[2].Name);
        Assert.Equal(typeof(int?), wire[2].ParameterType);

        Assert.Equal("ProviderLatenessMinutes", wire[3].Name);
        Assert.Equal(typeof(int?), wire[3].ParameterType);
        Assert.True(wire[3].HasDefaultValue);
        Assert.Null(wire[3].DefaultValue);

        Assert.Equal("CustomerConsentedToLateness", wire[4].Name);
        Assert.Equal(typeof(bool), wire[4].ParameterType);
        Assert.True(wire[4].HasDefaultValue);
        Assert.Equal(false, wire[4].DefaultValue);

        Assert.Equal("ForceMajeure", wire[5].Name);
        Assert.Equal(typeof(bool), wire[5].ParameterType);
        Assert.True(wire[5].HasDefaultValue);
        Assert.Equal(false, wire[5].DefaultValue);
    }

    [Fact]
    public void AdminReconcileRefundAmbiguityRequest_KeepsItsWireShape()
    {
        ConstructorInfo constructor = Assert.Single(
            typeof(AdminReconcileRefundAmbiguityRequest).GetConstructors());

        ParameterInfo[] wire = constructor.GetParameters();

        Assert.Equal(4, wire.Length);

        Assert.Equal("Evidence", wire[0].Name);
        Assert.Equal(typeof(string), wire[0].ParameterType);

        Assert.Equal("RefundDidMove", wire[1].Name);
        Assert.Equal(typeof(bool), wire[1].ParameterType);

        Assert.Equal("ExpectedAttemptClaimId", wire[2].Name);
        Assert.Equal(typeof(Guid?), wire[2].ParameterType);
        Assert.True(wire[2].HasDefaultValue);
        Assert.Null(wire[2].DefaultValue);

        Assert.Equal("VerifiedProviderReference", wire[3].Name);
        Assert.Equal(typeof(string), wire[3].ParameterType);
        Assert.True(wire[3].HasDefaultValue);
        Assert.Null(wire[3].DefaultValue);
    }

    private enum HttpVerb
    {
        Get,
        Post
    }

    private static void ProducingFor(
        string methodName,
        HttpVerb verb,
        (int StatusCode, Type? ResponseType)[] expected,
        string? routeTemplate)
    {
        MethodInfo method = typeof(AdminBookingsController).GetMethod(methodName)!;

        HttpMethodAttribute httpAttribute = Assert.IsAssignableFrom<HttpMethodAttribute>(
            method.GetCustomAttributes(true).Single(
                attribute => attribute is HttpMethodAttribute));
        Assert.Equal(routeTemplate, httpAttribute.Template);

        if (verb == HttpVerb.Post)
        {
            Assert.IsType<HttpPostAttribute>(httpAttribute);
        }
        else
        {
            Assert.IsType<HttpGetAttribute>(httpAttribute);
        }

        (int StatusCode, Type? ResponseType)[] actual = method
            .GetCustomAttributes<ProducesResponseTypeAttribute>(false)
            .Select(attribute => (
                attribute.StatusCode,
                attribute.Type == typeof(void) ? null : attribute.Type))
            .OrderBy(entry => entry.StatusCode)
            .ToArray();

        Assert.Equal(expected, actual);
    }
}
