namespace Sanad.API.Authorization;

public static class AuthorizationPolicies
{
    public const string NormalAccess =
        "NormalAccess";

    public const string CmsContent =
        "CmsContent";

    public const string CommunityModeration = "CommunityModeration";

    public const string CaregiversAdmin =
        "CaregiversAdmin";

    public const string CaregiverReviewAdmin =
        "CaregiverReviewAdmin";

    public const string CareHomesOperationalAdmin =
        "CareHomesOperationalAdmin";

    public const string SubscriptionPlanAdmin =
        "SubscriptionPlanAdmin";

    public const string CaregiverAccess =
        "CaregiverAccess";

    public const string FamilyAccess =
        "FamilyAccess";

    public const string CareHomeOwnerAccess =
        "CareHomeOwnerAccess";

    public const string ElderlyAccess =
        "ElderlyAccess";

    public const string ElderlyMedicationOperationalRead =
        "ElderlyMedicationOperationalRead";
    public const string ElderlyMedicationOperationalManage =
        "ElderlyMedicationOperationalManage";
    public const string ElderlyHelpRequestOperational = "ElderlyHelpRequestOperational";
    public const string ElderlySosOperational = "ElderlySosOperational";
    public const string AdminNotificationOperationalRead = "AdminNotificationOperationalRead";
    public const string FinanceOperationalAdmin = "FinanceOperationalAdmin";
    public const string PayoutOperationalAdmin = "PayoutOperationalAdmin";

    /// <summary>Chat support routes (endpoints 12–15): SuperAdmin +
    /// SupportAdmin, mirroring the elderly-ops operational policies.</summary>
    public const string ChatSupportAdmin = "ChatSupportAdmin";

    /// <summary>
    /// Sensitive Chat access: Normal access plus a verified session-bound
    /// token (<c>sid</c> claim). Legacy unbound Normal tokens are rejected
    /// here while other surfaces keep the documented rollout window.
    /// </summary>
    public const string SessionBoundAccess = "SessionBoundAccess";

    public const string SuperAdminOnly = "SuperAdminOnly";
}
