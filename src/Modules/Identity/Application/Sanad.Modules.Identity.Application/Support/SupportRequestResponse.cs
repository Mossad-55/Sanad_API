using Sanad.BuildingBlocks.Domain.Primitives.Ids;
using Sanad.Modules.Identity.Domain.Support;

namespace Sanad.Modules.Identity.Application.Support;

public sealed record SupportRequestResponse(
    SupportTicketId TicketId,
    SupportTicketStatus Status,
    DateTime SubmittedOnUtc);
