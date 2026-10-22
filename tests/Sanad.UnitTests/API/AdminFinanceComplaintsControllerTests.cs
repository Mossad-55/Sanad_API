using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Sanad.API.Authorization;
using Sanad.API.Controllers;

namespace Sanad.UnitTests.API;

/// <summary>
/// W3-F3 presentation contract for the reviewed-complaint and platform-shutdown
/// refund surfaces. The refund-workflow policy is SuperAdmin+SupportAdmin and
/// deliberately excludes FinanceAdmin; the roles are kept apart in the
/// application seam AND at the endpoint (M1): SupportAdmin investigates, and the
/// approve/refuse verdict routes additionally carry the SuperAdminOnly policy.
/// <para>NOT RUN: authored for the W3 integration gate; the Mastermind runs it.</para>
/// </summary>
public sealed class AdminFinanceComplaintsControllerTests
{
    [Fact]
    public void Complaint_review_controller_requires_the_refund_workflow_policy()
    {
        var authorization = Assert.Single(typeof(AdminFinanceComplaintsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.RefundWorkflowAdmin, authorization.Policy);
        Assert.Equal("api/v1/admin/finance/complaint-reviews",
            Assert.Single(typeof(AdminFinanceComplaintsController).GetCustomAttributes<RouteAttribute>()).Template);
    }

    [Fact]
    public void Complaint_review_routes_are_stable()
    {
        Assert.Null(typeof(AdminFinanceComplaintsController).GetMethod(nameof(AdminFinanceComplaintsController.Open))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Null(typeof(AdminFinanceComplaintsController).GetMethod(nameof(AdminFinanceComplaintsController.List))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{complaintReviewId:guid}", typeof(AdminFinanceComplaintsController).GetMethod(nameof(AdminFinanceComplaintsController.Get))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("by-refund-case/{refundCaseId:guid}", typeof(AdminFinanceComplaintsController).GetMethod(nameof(AdminFinanceComplaintsController.GetForCase))!
            .GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.Equal("{complaintReviewId:guid}/investigate", typeof(AdminFinanceComplaintsController).GetMethod(nameof(AdminFinanceComplaintsController.Investigate))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{complaintReviewId:guid}/approve", typeof(AdminFinanceComplaintsController).GetMethod(nameof(AdminFinanceComplaintsController.Approve))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.Equal("{complaintReviewId:guid}/refuse", typeof(AdminFinanceComplaintsController).GetMethod(nameof(AdminFinanceComplaintsController.Refuse))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void The_verdict_routes_are_SuperAdminOnly_while_investigate_stays_refund_workflow()
    {
        // M1: approve/refuse carry a route-level SuperAdminOnly policy on top of
        // the class-level RefundWorkflowAdmin policy, so a SupportAdmin token is
        // refused before the handler. Investigate/list/open stay on the class
        // policy (SuperAdmin or SupportAdmin).
        var approve = typeof(AdminFinanceComplaintsController)
            .GetMethod(nameof(AdminFinanceComplaintsController.Approve))!
            .GetCustomAttributes<AuthorizeAttribute>(true).Single();
        var refuse = typeof(AdminFinanceComplaintsController)
            .GetMethod(nameof(AdminFinanceComplaintsController.Refuse))!
            .GetCustomAttributes<AuthorizeAttribute>(true).Single();

        Assert.Equal(AuthorizationPolicies.SuperAdminOnly, approve.Policy);
        Assert.Equal(AuthorizationPolicies.SuperAdminOnly, refuse.Policy);

        Assert.Empty(typeof(AdminFinanceComplaintsController)
            .GetMethod(nameof(AdminFinanceComplaintsController.Investigate))!
            .GetCustomAttributes<AuthorizeAttribute>(true));
        Assert.Empty(typeof(AdminFinanceComplaintsController)
            .GetMethod(nameof(AdminFinanceComplaintsController.Open))!
            .GetCustomAttributes<AuthorizeAttribute>(true));
    }

    [Fact]
    public void The_approve_request_carries_an_optional_award_and_evidence_reference()
    {
        // M2: approval takes an award (long, defaults to 0 per #31) plus an
        // optional evidence reference; the movement is appended under the case id.
        Assert.Contains(typeof(ApproveComplaintReviewRequest).GetProperties(),
            property => property.Name == "AwardMinorUnits" && property.PropertyType == typeof(long));
        Assert.Contains(typeof(ApproveComplaintReviewRequest).GetProperties(),
            property => property.Name == "EvidenceReference" && property.PropertyType == typeof(string));
    }

    [Fact]
    public void The_shutdown_request_carries_no_caller_supplied_money()
    {
        // M4/M5: the proration base and cap are derived server-side from succeeded
        // payment attempts, so the request exposes no prior-refund or paid figure.
        string[] names = typeof(RefundSubscriptionServiceShutdownRequest).GetProperties()
            .Where(property => property.Name != "EqualityContract")
            .Select(property => property.Name).ToArray();

        Assert.DoesNotContain("PriorRefundedMinorUnits", names);
        Assert.Contains("SubscriptionId", names);
        Assert.Contains("ShutdownOnUtc", names);
        Assert.Contains("IdempotencyKey", names);
    }

    [Fact]
    public void Subscription_shutdown_refund_controller_requires_the_refund_workflow_policy()
    {
        var authorization = Assert.Single(typeof(AdminFinanceSubscriptionRefundsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());

        Assert.Equal(AuthorizationPolicies.RefundWorkflowAdmin, authorization.Policy);
        Assert.Equal("api/v1/admin/finance/subscription-refunds",
            Assert.Single(typeof(AdminFinanceSubscriptionRefundsController).GetCustomAttributes<RouteAttribute>()).Template);
        Assert.Equal("service-shutdown", typeof(AdminFinanceSubscriptionRefundsController)
            .GetMethod(nameof(AdminFinanceSubscriptionRefundsController.RefundServiceShutdown))!
            .GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    [Fact]
    public void The_family_cancel_renewal_route_accepts_an_optional_idempotency_key_body()
    {
        MethodInfo cancel = typeof(FamilySubscriptionsController)
            .GetMethod(nameof(FamilySubscriptionsController.CancelRenewal))!;

        Assert.Equal("cancel-renewal", cancel.GetCustomAttribute<HttpPostAttribute>()!.Template);
        ParameterInfo body = cancel.GetParameters()[0];
        Assert.Equal(typeof(CancelSubscriptionRenewalRequest), body.ParameterType);
        Assert.Equal(
            new[] { "IdempotencyKey" },
            typeof(CancelSubscriptionRenewalRequest).GetProperties()
                .Where(property => property.Name != "EqualityContract")
                .Select(property => property.Name).ToArray());
    }
}
