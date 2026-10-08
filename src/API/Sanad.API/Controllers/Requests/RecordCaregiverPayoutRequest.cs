namespace Sanad.API.Controllers.Requests;

public sealed record RecordCaregiverPayoutRequest(
    Guid BookingId,
    string TransferReference,
    string Evidence,
    string Reason);
