using Ahlena.BuildingBlocks.Domain.Primitives.Ids;
using Ahlena.Modules.Identity.Domain.Support;

namespace Ahlena.Modules.Identity.Application.Support;

public sealed record SupportRequestResponse(
    SupportTicketId TicketId,
    SupportTicketStatus Status,
    DateTime SubmittedOnUtc);
