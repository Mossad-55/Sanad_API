using Sanad.BuildingBlocks.Application.Results;

namespace Sanad.Modules.Chat.Application.Blocks;

/// <summary>
/// Endpoint 9 failure codes. <see cref="Invalid"/> answers the blocker's own
/// mistake (self-block) as a 400 — it reveals nothing the caller does not
/// already know. <see cref="NotFound"/> (unknown target user) is forced by
/// the Chat controller into the same generic privacy 404 as an unknown
/// conversation, so this endpoint never confirms whether an arbitrary user
/// id is registered. Unblock deliberately has no codes: a missing row
/// succeeds (204 idempotent) and never reveals whether a block existed.
/// </summary>
public static class BlockErrors
{
    public static readonly Error Invalid =
        new("Chat.Block.Invalid", "Users cannot block themselves.");

    public static readonly Error NotFound =
        new("Chat.Block.NotFound", "Blocked user not found.");
}
