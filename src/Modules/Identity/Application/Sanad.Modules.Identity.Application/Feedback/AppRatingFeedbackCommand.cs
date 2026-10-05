using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Application.Results;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Identity.Application.Feedback;

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