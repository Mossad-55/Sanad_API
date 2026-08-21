namespace Sanad.Modules.Identity.Application.Authentication.Sessions;

public sealed record ActiveSessionsResponse(
    IReadOnlyList<ActiveSessionItem> Sessions);