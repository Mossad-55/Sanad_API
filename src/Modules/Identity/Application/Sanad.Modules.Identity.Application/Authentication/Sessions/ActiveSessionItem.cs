using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed record ActiveSessionItem(
    DeviceSessionId DeviceSessionId,
    string DeviceName,
    DevicePlatform Platform,
    string AppVersion,
    DateTime CreatedOnUtc,
    DateTime ExpiresOnUtc,
    DateTime? LastRotatedOnUtc);