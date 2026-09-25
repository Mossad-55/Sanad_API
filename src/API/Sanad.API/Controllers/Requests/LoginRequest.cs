using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.API.Controllers.Requests;

public sealed record LoginRequest
{
    public string? Identifier { get; init; }

    public string? Email { get; init; }

    public string Password { get; init; } = string.Empty;

    public string DeviceName { get; init; } = string.Empty;

    public DevicePlatform DevicePlatform { get; init; }

    public string AppVersion { get; init; } = string.Empty;

    public string LoginIdentifier =>
        (Identifier is null) != (Email is null)
            ? Identifier ?? Email!
            : string.Empty;
}
