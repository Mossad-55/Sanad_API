namespace Sanad.Modules.Identity.Infrastructure.Messaging.EmailTemplates;

/// <summary>The complete set of transactional email variants approved for Notifications V1.</summary>
public enum EmailTemplateKind
{
    OtpCode,
    Invitation,
    SupportAcknowledgment,
    BookingPaid,
    BookingAccepted,
    BookingDeclined,
    BookingStarted,
    BookingCompleted,
    BookingCancelledWithRefund,
    SubscriptionRenewed,
    SubscriptionFailed,
    SubscriptionChanged,
    PayoutRecorded,
    PayoutFailed,
    PostPublished,
    PostRejected
}
