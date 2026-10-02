namespace Sanad.API.Authorization;

public static class AuthorizationPolicies
{
    public const string NormalAccess =
        "NormalAccess";

    public const string CmsContent =
        "CmsContent";

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
}
