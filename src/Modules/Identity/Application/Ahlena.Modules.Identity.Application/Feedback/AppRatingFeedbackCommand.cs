using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Identity.Application.Feedback;

public sealed record AppRatingFeedbackCommand(
    Guid UserId,
    int Rating,
    string? Comment,
    string? DeviceInfo,
    string? AppVersion) : ICommand<bool>;

public sealed record AppRatingFeedbackResponse(
    bool Success,
    string? Message,
    Guid? FeedbackId);