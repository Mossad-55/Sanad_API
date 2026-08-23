using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.API.Controllers.Requests;

public sealed record LoginRequest(
    string Email,
    string Password,
    string DeviceName,
    DevicePlatform DevicePlatform,
    string AppVersion);