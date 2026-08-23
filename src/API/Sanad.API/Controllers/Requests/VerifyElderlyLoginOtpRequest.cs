using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.API.Controllers.Requests;

public sealed record VerifyElderlyLoginOtpRequest(
    string PhoneNumber,
    string Code,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion);