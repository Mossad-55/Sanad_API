using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Sanad.API.Controllers;
using System.Reflection;

namespace Sanad.UnitTests.API;

public sealed class NewEndpointsAuthorizationContractTests
{
    [Theory]
    [InlineData(typeof(CaregiverAvailabilityController), "GetAvailability", "api/v1/caregivers", "{caregiverId:guid}/availability")]
    [InlineData(typeof(CaregiverEarningsController), "GetEarningsSummary", "api/v1/caregivers", "{caregiverId:guid}/earnings/summary")]
    [InlineData(typeof(CaregiverEarningsController), "GetEarningsTransactions", "api/v1/caregivers", "{caregiverId:guid}/earnings/transactions")]
    [InlineData(typeof(CaregiverMedicationTasksController), "GetMedicationTasks", "api/v1/caregivers", "{caregiverId:guid}/medication-tasks")]
    [InlineData(typeof(CaregiverMedicationTasksController), "AdministerMedicationTask", "api/v1/caregivers", "{caregiverId:guid}/medication-tasks/{taskId}/administer")]
    [InlineData(typeof(CaregiverMedicationTasksController), "SkipMedicationTask", "api/v1/caregivers", "{caregiverId:guid}/medication-tasks/{taskId}/skip")]
    [InlineData(typeof(FeedbackController), "SubmitAppRating", "api/v1/feedback", "app-rating")]
    [InlineData(typeof(FamilyBookingsController), "AddBookingReview", "api/v1/family/bookings", "{bookingId:guid}/review")]
    [InlineData(typeof(FamilyController), "AcknowledgeDependentHelpRequest", "api/v1/family", "dependents/{dependentId:guid}/help-requests/{requestId:guid}/acknowledge")]
    [InlineData(typeof(CommunityPostsController), "GetPosts", "api/v1/community", "posts")]
    [InlineData(typeof(CommunityPostsController), "GetRecommendations", "api/v1/community", "recommendations")]
    [InlineData(typeof(CommunityPostsController), "CreatePost", "api/v1/community", "posts")]
    [InlineData(typeof(CommentsController), "CreateComment", "api/v1/community", "posts/{postId:guid}/comments")]
    [InlineData(typeof(CommentsController), "UpdateComment", "api/v1/community", "comments/{commentId:guid}")]
    [InlineData(typeof(DependentMedicalAccessGrantsController), "CreateMedicalAccessGrant", "api/v1/family/dependents", "{dependentId:guid}/medical-access-grants")]
    [InlineData(typeof(DependentMedicalAccessGrantsController), "DeleteMedicalAccessGrant", "api/v1/family/dependents", "{dependentId:guid}/medical-access-grants/{grantId:guid}")]
    public void NewEndpoint_IsAuthenticatedAndHasExpectedRoute(
        Type controllerType,
        string methodName,
        string route,
        string actionRoute)
    {
        Assert.NotNull(controllerType.GetCustomAttributes<AuthorizeAttribute>().SingleOrDefault());
        Assert.Equal(route, controllerType.GetCustomAttributes<RouteAttribute>().Single().Template);

        var method = controllerType.GetMethod(methodName);
        Assert.NotNull(method);
        var httpRoute = method!.GetCustomAttributes<HttpMethodAttribute>().Single();
        Assert.Equal(actionRoute, httpRoute.Template);
    }

    [Fact]
    public void FamilyStateChangingEndpoints_RequireFamilyAuthorization()
    {
        Assert.Equal("FamilyAccess", GetAuthorizePolicy(typeof(FamilyBookingsController)));
        Assert.Equal("FamilyAccess", GetAuthorizePolicy(typeof(FamilyController)));
        Assert.Equal("FamilyAccess", GetAuthorizePolicy(typeof(DependentMedicalAccessGrantsController)));
    }

    [Fact]
    public void PayoutAccountEndpoints_RequireCaregiverAccess()
    {
        Assert.Equal("CaregiverAccess", GetAuthorizePolicy(typeof(CaregiverPayoutAccountController)));
        Assert.Equal(
            "api/v1/caregiver/payout-account",
            typeof(CaregiverPayoutAccountController).GetCustomAttributes<RouteAttribute>().Single().Template);
        Assert.NotNull(typeof(CaregiverPayoutAccountController).GetMethod(nameof(CaregiverPayoutAccountController.GetPayoutAccount)));
        Assert.NotNull(typeof(CaregiverPayoutAccountController).GetMethod(nameof(CaregiverPayoutAccountController.UpdatePayoutAccount)));
    }

    private static string? GetAuthorizePolicy(Type controllerType)
    {
        return controllerType.GetCustomAttributes<AuthorizeAttribute>().Single().Policy;
    }
}
