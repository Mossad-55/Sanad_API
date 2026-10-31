using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Ahlena.API.Controllers;
using Ahlena.Modules.Caregivers.Application.PayoutAccounts;
using Ahlena.Modules.Caregivers.Application.Payouts;
using Ahlena.Modules.CareHomes.Application.Bookings;
using Ahlena.Modules.CareHomes.Application.Facilities;
using Ahlena.Modules.Cms.Application.MedicationLateness;
using Ahlena.Modules.Finance.Application;
using Ahlena.Modules.Families.Application.Subscriptions;
using Ahlena.Modules.Identity.Application.Feedback;

namespace Ahlena.UnitTests.API;

public sealed class AdminResponseSchemaContractTests
{
    [Fact]
    public void CareHomeAdminRoutes_DeclareTheirSuccessResponseSchemas()
    {
        AssertResponse<AdminCareHomeBookingPage>(typeof(AdminCareHomeBookingsController), "List", 200);
        AssertResponse<AdminCareHomeBookingDetail>(typeof(AdminCareHomeBookingsController), "Detail", 200);
        AssertResponse<CareHomeDashboard>(typeof(AdminCareHomeFinanceController), "Dashboard", 200);
        AssertResponse<CareHomeRevenueReport>(typeof(AdminCareHomeFinanceController), "Revenue", 200);
        AssertResponse<CareHomeReceipt>(typeof(AdminCareHomeFinanceController), "Receipt", 200);
        AssertResponse<IReadOnlyList<CareHomeInternalBookingNoteItem>>(typeof(AdminCareHomeFinanceController), "Notes", 200);
        AssertResponse<CareHomeInternalBookingNoteItem>(typeof(AdminCareHomeFinanceController), "AddNote", 200);
        AssertResponse<IReadOnlyList<CareHomePayoutLedgerItem>>(typeof(AdminCareHomePayoutsController), "Ledger", 200);
        AssertResponse<CareHomePayoutLedgerItem>(typeof(AdminCareHomePayoutsController), "Record", 200);
        AssertResponse<CareHomePayoutLedgerItem>(typeof(AdminCareHomePayoutsController), "RecordReversal", 200);
        AssertResponse<CareHomeBookingResponse>(typeof(AdminCareHomeRefundsController), "Retry", 200);
        AssertResponse<CareHomeBookingResponse>(typeof(AdminCareHomeRefundsController), "RecordCompleted", 200);
    }

    [Fact]
    public void SettingsAndConfigurationRoutes_DeclareTheirSuccessResponseSchemas()
    {
        AssertResponse<MedicationLatenessSettingResponse>(typeof(AdminMedicationLatenessController), "Get", 200);
        AssertResponse<MedicationLatenessSettingResponse>(typeof(AdminMedicationLatenessController), "Create", 200);
        AssertResponse<Guid>(typeof(AdminCaregiverPayoutPolicyController), "Create", 201);
        AssertResponse<CaregiverPayoutPolicyResponse>(typeof(AdminCaregiverPayoutPolicyController), "Current", 200);
        AssertResponse<IReadOnlyList<CaregiverPayoutPolicyResponse>>(typeof(AdminCaregiverPayoutPolicyController), "History", 200);
        AssertResponse<Guid>(typeof(AdminSubscriptionsController), "CreateTaxRule", 201);
        AssertResponse<SubscriptionTaxRuleResponse>(typeof(AdminSubscriptionsController), "GetCurrentTaxRule", 200);
        AssertResponse<IReadOnlyList<SubscriptionTaxRuleResponse>>(typeof(AdminSubscriptionsController), "GetTaxRuleHistory", 200);
        AssertResponse<CareHomeProfileMediaResponse>(typeof(CareHomeProfileMediaController), "Upload", 200);
        AssertResponse<FileStreamResult>(typeof(CareHomeProfileMediaController), "ReadOwner", 200);
        AssertResponse<PagedAdminAppRatings>(typeof(AdminFeedbackAppRatingsController), "ListAppRatings", 200);
    }

    [Fact]
    public void PayoutStatusTextFields_DeclareAllowedValues()
    {
        AssertAllowedValues(typeof(CaregiverPayoutResponse), "Status", "Paid", "Failed");
        AssertAllowedValues(typeof(PayoutAccountAdminItem), "Status", "Pending", "Verified", "Rejected", "Revoked");
        AssertAllowedValues(typeof(PayoutAccountAdminDetailResponse), "Status", "Pending", "Verified", "Rejected", "Revoked");
    }

    private static void AssertResponse<TResponse>(Type controller, string actionName, int statusCode)
    {
        MethodInfo? action = controller.GetMethod(actionName);
        Assert.NotNull(action);
        var response = Assert.Single(action.GetCustomAttributes<ProducesResponseTypeAttribute>(),
            attribute => attribute.StatusCode == statusCode);
        Assert.Equal(typeof(TResponse), response.Type);
    }

    private static void AssertAllowedValues(Type responseType, string propertyName, params string[] expected)
    {
        PropertyInfo? property = responseType.GetProperty(propertyName);
        Assert.NotNull(property);
        var allowedValues = Assert.Single(property.GetCustomAttributes<AllowedValuesAttribute>());
        Assert.Equal(expected, allowedValues.Values.Cast<string>());
    }
}
