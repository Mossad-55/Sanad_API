using Microsoft.EntityFrameworkCore;
using Ahlena.BuildingBlocks.Application.CQRS;
using Ahlena.BuildingBlocks.Application.Results;
using Ahlena.Modules.Chat.Application.Abstractions.Data;
using Ahlena.Modules.Chat.Domain.Blocks;

namespace Ahlena.Modules.Chat.Application.Blocks;

public sealed class UnblockUserCommandHandler
    : ICommandHandler<UnblockUserCommand>
{
    private readonly IChatDbContext _dbContext;

    public UnblockUserCommandHandler(IChatDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result> Handle(
        UnblockUserCommand request,
        CancellationToken cancellationToken)
    {
        Block? block = await _dbContext.Blocks.SingleOrDefaultAsync(
            item =>
                item.BlockerUserId == request.Blocker &&
                item.BlockedUserId == request.Blocked,
            cancellationToken);

        // A missing row still succeeds (204 idempotent). Unblock is the
        // blocker's own action, so always-204 reveals nothing to a third
        // party and keeps client retries safe. Removing the row is the
        // complete Lane B enforcement off-switch: send-time checks consult
        // Blocks directly, so no row means sending is restored.
        if (block is not null)
        {
            _dbContext.Blocks.Remove(block);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return Result.Success();
    }
}
