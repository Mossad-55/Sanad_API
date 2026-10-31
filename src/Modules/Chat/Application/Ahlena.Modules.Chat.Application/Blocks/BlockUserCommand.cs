using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Application.Blocks;

/// <summary>
/// Endpoint 9 (POST blocks) — the blocker blocks the given user. Idempotent:
/// blocking an already-blocked user succeeds again (204) without creating a
/// duplicate row.
/// </summary>
public sealed record BlockUserCommand(
    UserId Blocker,
    UserId Blocked)
    : ICommand;
