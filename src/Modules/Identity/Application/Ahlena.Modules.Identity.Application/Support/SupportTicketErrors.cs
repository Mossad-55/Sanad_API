using Ahlena.BuildingBlocks.Application.Results;

namespace Ahlena.Modules.Identity.Application.Support;

public static class SupportTicketErrors
{
    public static readonly Error NotFound =
        new("Identity.Support.NotFound", "Support ticket was not found.");

    public static readonly Error Conflict =
        new("Identity.Support.Conflict", "The requested support ticket operation conflicts with the current state.");
}