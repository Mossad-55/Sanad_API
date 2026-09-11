using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Support;

public sealed record SubmitSupportRequestCommand(
    UserId CurrentUserId,
    string Subject,
    string Message)
    : ICommand<SupportRequestResponse>;
