using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Domain.Authentication.DeviceSessions;

namespace Ahlena.Modules.Identity.Application.Authentication.Sessions;

public sealed record ActiveSessionItem(
    DeviceSessionId DeviceSessionId,
    string DeviceName,
    DevicePlatform Platform,
    string AppVersion,
    DateTime CreatedOnUtc,
    DateTime ExpiresOnUtc,
    DateTime? LastRotatedOnUtc);