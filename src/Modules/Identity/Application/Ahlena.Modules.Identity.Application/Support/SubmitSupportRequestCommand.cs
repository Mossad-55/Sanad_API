using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Domain.Support;

namespace Ahlena.Modules.Identity.Application.Support;

public sealed record SubmitSupportRequestCommand(
    UserId CurrentUserId,
    string Subject,
    string Message)
    : ICommand<SupportRequestResponse>;
