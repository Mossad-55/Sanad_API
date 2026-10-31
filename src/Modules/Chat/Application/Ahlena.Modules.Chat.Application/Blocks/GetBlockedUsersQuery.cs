using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Domain.Primitives.Ids;

namespace Ahlena.Modules.Chat.Application.Blocks;

/// <summary>
/// Endpoint 9 (GET blocks) — the requester's own blocked user ids, ordered
/// most-recent block first (CreatedOnUtc descending). Per-user: never
/// includes ids blocked by anyone else.
/// </summary>
public sealed record GetBlockedUsersQuery(
    UserId Requester)
    : IQuery<Guid[]>;
