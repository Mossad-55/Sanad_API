using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Domain;

public sealed record Feedback(
    Guid Id,
    UserId UserId,
    int Rating,
    string? Comment,
    string? DeviceInfo,
    string? AppVersion,
    DateTime CreatedOnUtc);