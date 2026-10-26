namespace Sanad.API.Authorization;

public static class AuthorizationPolicies
{
    public const string NormalAccess =
        "NormalAccess";

    public const string CmsContent =
        "CmsContent";

    /// <summary>
    /// SET-13 support contact pair: authenticated SuperAdmin, SupportAdmin or
    /// ContentAdmin on a Normal token. Scoped to the two
    /// <c>/admin/help-center/support-contact</c> actions so support staff can
    /// manage the contact card without gaining FAQ/CMS write authority, which
    /// stays with <see cref="CmsContent"/>.
    /// </summary>
    public const string CmsHelpContact =
        "CmsHelpContact";

    public const string CommunityModeration = "CommunityModeration";

    public const string CaregiversAdmin =
        "CaregiversAdmin";

    public const string CaregiverReviewAdmin =
        "CaregiverReviewAdmin";

    /// <summary>
    /// Dedicated caregiver name+id lookup only
    /// (<c>GET /api/v1/admin/caregivers/lookup</c>): authenticated SuperAdmin or
    /// FinanceAdmin on a Normal token. Deliberately NOT the shared
    /// <see cref="CaregiverReviewAdmin"/>: the caregiver review/ratings
    /// surfaces keep SuperAdmin + SupportAdmin, SupportAdmin is explicitly
    /// removed from this lookup, and ContentAdmin and every app audience are
    /// denied. The policy grants names and caregiver ids only — no contact
    /// data is added.
    /// </summary>
    public const string CaregiverLookupAdmin = "CaregiverLookupAdmin";

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

    public const string MedicalAccessAuditAdmin =
        "MedicalAccessAuditAdmin";
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

    /// <summary>
    /// F5 support inbox: authenticated SuperAdmin + SupportAdmin management of
    /// retained support tickets (list/detail/reply/status). ContentAdmin and
    /// FinanceAdmin are deliberately excluded, as are all app audiences.
    /// </summary>
    public const string SupportInboxAdmin = "SupportInboxAdmin";

    /// <summary>
    /// F6 new operational reads: authenticated SuperAdmin + SupportAdmin only.
    /// Used by read-only operational surfaces that must not widen existing
    /// Finance access or any app audience's access to family data.
    /// </summary>
    public const string OperationalReadAdmin = "OperationalReadAdmin";

    /// <summary>
    /// E04 platform controls: authenticated SuperAdmin-only read/update of the
    /// operational/maintenance state and Card/Wallet switches.
    /// </summary>
    public const string PlatformControlsAdmin = "PlatformControlsAdmin";

    /// <summary>
    /// W2-F0 refund workflow money core: authenticated SuperAdmin + SupportAdmin
    /// on a Normal token. SupportAdmin receives and investigates refund
    /// requests, SuperAdmin approves reviewed financial awards and exceptional
    /// adjustments (refund contract decision #12). FinanceAdmin and every app
    /// audience are deliberately excluded, and this policy grants nothing over
    /// the existing shipment/payout or CMS surfaces.
    /// </summary>
    public const string RefundWorkflowAdmin = "RefundWorkflowAdmin";
}
