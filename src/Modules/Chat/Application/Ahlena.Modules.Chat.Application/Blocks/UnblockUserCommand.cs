using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Application.Blocks;

/// <summary>
/// Endpoint 9 (DELETE blocks/{userId}) — the blocker removes their block on
/// the given user. Idempotent: a missing row still succeeds (204) and never
/// reveals whether a block existed. Unblock restores sending.
/// </summary>
public sealed record UnblockUserCommand(
    UserId Blocker,
    UserId Blocked)
    : ICommand;
