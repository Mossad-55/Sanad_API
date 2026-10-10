using Sanad.BuildingBlocks.Application.CQRS;
using Sanad.BuildingBlocks.Domain.Primitives.Ids;

namespace Sanad.Modules.Chat.Application.Blocks;

/// <summary>
/// Endpoint 9 (GET blocks) — the requester's own blocked user ids, ordered
/// most-recent block first (CreatedOnUtc descending). Per-user: never
/// includes ids blocked by anyone else.
/// </summary>
public sealed record GetBlockedUsersQuery(
    UserId Requester)
    : IQuery<Guid[]>;
