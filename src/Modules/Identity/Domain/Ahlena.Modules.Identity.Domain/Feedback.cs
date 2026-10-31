using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Domain;

public sealed record Feedback(
    Guid Id,
    UserId UserId,
    int Rating,
    string? Comment,
    string? DeviceInfo,
    string? AppVersion,
    DateTime CreatedOnUtc);